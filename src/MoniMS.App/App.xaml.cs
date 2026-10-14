using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MoniMS.App.Interop;
using MoniMS.App.Logging;
using MoniMS.App.Services;
using MoniMS.App.ViewModels;
using MoniMS.App.Views;
using MoniMS.Core;
using MoniMS.Core.Desktop;
using MoniMS.Core.Media;
using MoniMS.Core.Presets;
using MoniMS.Core.Settings;
using MoniMS.Core.SystemInfo;

namespace MoniMS.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Disposed in OnExit")]
public partial class App : Application
{
    private const string MutexName = @"Local\MoniMS.SingleInstance";
    private const string ActivateEventName = @"Local\MoniMS.Activate";
    private const string ExitEventName = @"Local\MoniMS.Exit";

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private EventWaitHandle? _exitEvent;
    private ServiceProvider? _services;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 중복 실행 방지: 같은 빌드가 이미 실행 중이면 그쪽 설정 창을 띄우고 종료.
        // 예전 빌드가 실행 중이면(업데이트 직후) 그걸 종료시키고 새 버전이 대신 실행된다.
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew && !TakeOverOlderInstance())
        {
            try
            {
                EventWaitHandle.OpenExisting(ActivateEventName).Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _services = ConfigureServices();
        _logger = _services.GetRequiredService<ILogger<App>>();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _logger.LogCritical(args.ExceptionObject as Exception, "Unhandled exception");

        var settings = _services.GetRequiredService<ISettingsStore>();
        UiThemeManager.Apply(settings.Current.UiTheme);
        var monitor = _services.GetRequiredService<ISystemMonitor>();
        monitor.Interval = TimeSpan.FromMilliseconds(Math.Clamp(settings.Current.RefreshIntervalMs, 250, 10000));
        monitor.Start();

        _services.GetRequiredService<WidgetController>().Initialize();
        _services.GetRequiredService<ImageWidgetController>().Initialize();
        _services.GetRequiredService<FileWidgetController>().Initialize();
        _services.GetRequiredService<MediaWidgetController>().Initialize();
        _services.GetRequiredService<TrayIconService>().Show();
        ListenForActivation();
        ListenForExit();

        _logger.LogInformation("MoniMS started");
        _ = CheckForUpdatesInBackgroundAsync();
        if (!e.Args.Contains("--background"))
            _services.GetRequiredService<WindowService>().ShowManager();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.AddDebug();
            b.AddProvider(new FileLoggerProvider(Path.Combine(AppPaths.DataDirectory, "logs")));
        });

        // Core
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<IPresetStore>(sp => new PresetStore(logger: sp.GetRequiredService<ILogger<PresetStore>>()));
        services.AddSingleton<IWallpaperService, WallpaperService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDesktopIconService, DesktopIconService>();
        services.AddSingleton<ISystemMonitor, SystemMonitor>();
        services.AddSingleton<PresetService>();
        services.AddSingleton<MediaSessions>();
        services.AddSingleton<AudioVolume>();

        // App
        services.AddSingleton<WidgetViewModel>();
        services.AddSingleton<WidgetController>();
        services.AddSingleton<IWidgetLayoutHost>(sp => sp.GetRequiredService<WidgetController>());
        services.AddSingleton<ImageStore>(_ => new ImageStore());
        services.AddSingleton<ImageWidgetController>();
        services.AddSingleton<IImageWidgetHost>(sp => sp.GetRequiredService<ImageWidgetController>());
        services.AddTransient<ImageWidgetViewModel>();
        services.AddSingleton<ShellIcons>();
        services.AddSingleton<FileWidgetViewModel>();
        services.AddSingleton<FileWidgetController>();
        services.AddSingleton<IFileWidgetHost>(sp => sp.GetRequiredService<FileWidgetController>());
        services.AddTransient<FileWidgetSettingsViewModel>();
        services.AddSingleton<MediaWidgetViewModel>();
        services.AddSingleton<MediaWidgetController>();
        services.AddSingleton<IMediaWidgetHost>(sp => sp.GetRequiredService<MediaWidgetController>());
        services.AddTransient<MediaWidgetSettingsViewModel>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<WindowService>();
        services.AddSingleton<UpdateService>();
        services.AddTransient<ManagerViewModel>();
        services.AddTransient<ManagerWindow>();

        return services.BuildServiceProvider();
    }

    /// <summary>업데이트 확인 시도 간격. Windows 시작 직후에는 아직 인터넷이 안 될 때가 많아서 실패하면 다시 시도한다.</summary>
    private static readonly TimeSpan[] UpdateCheckDelays =
        [TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30)];

    /// <summary>시작하고 잠시 뒤 업데이트를 확인해서, 있으면 트레이 알림만 띄운다.</summary>
    private async Task CheckForUpdatesInBackgroundAsync()
    {
        for (var attempt = 0; attempt < UpdateCheckDelays.Length; attempt++)
        {
            await Task.Delay(UpdateCheckDelays[attempt]);
            try
            {
                var updates = _services?.GetRequiredService<UpdateService>();
                if (updates is null || !updates.IsSupported)
                    return;
                var info = await updates.CheckAsync();
                if (info is not null)
                {
                    _services?.GetRequiredService<TrayIconService>().Notify("Update available",
                        $"MoniMS {info.TargetFullRelease.Version} is ready. Open Settings → General to update.");
                }
                return;
            }
            catch (Exception ex) when (_services is not null)
            {
                if (attempt == UpdateCheckDelays.Length - 1)
                    _logger?.LogWarning(ex, "Background update check failed");
                else
                    _logger?.LogInformation("Background update check failed, retrying later: {Message}", ex.Message);
            }
            catch (Exception)
            {
                return; // 앱이 끝나는 중
            }
        }
    }

    /// <summary>
    /// 실행 중인 다른 MoniMS가 지금 exe 파일보다 먼저 시작됐다면 예전 빌드로 보고 종료시킨다.
    /// (같은 exe를 두 번 실행한 경우는 false → 기존 인스턴스 활성화)
    /// </summary>
    private bool TakeOverOlderInstance()
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
            return false;
        var builtAt = File.GetLastWriteTimeUtc(exe);
        var myId = Environment.ProcessId;

        var older = System.Diagnostics.Process.GetProcesses()
            .Where(p => p.Id != myId && p.ProcessName.StartsWith("MoniMS", StringComparison.OrdinalIgnoreCase))
            .Where(p =>
            {
                try
                {
                    return p.StartTime.ToUniversalTime() < builtAt;
                }
                catch (Exception)
                {
                    return false; // 권한 없음 등
                }
            })
            .ToList();
        if (older.Count == 0)
            return false;

        // 새 빌드는 종료 이벤트를 듣는다. 예전 빌드는 못 들으므로 잠시 기다린 뒤 강제 종료.
        try
        {
            EventWaitHandle.OpenExisting(ExitEventName).Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
        foreach (var p in older)
        {
            try
            {
                if (!p.WaitForExit(3000))
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
            }
            catch (Exception)
            {
            }
        }

        try
        {
            return _mutex!.WaitOne(TimeSpan.FromSeconds(5));
        }
        catch (AbandonedMutexException)
        {
            return true; // 강제 종료된 쪽이 놓고 간 뮤텍스 → 소유권은 우리에게 넘어옴
        }
    }

    /// <summary>다른(새) 버전이 실행되면서 종료를 요청하면 깔끔하게 종료.</summary>
    private void ListenForExit()
    {
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        _exitEvent.Reset(); // 우리가 예전 인스턴스에게 보낸 신호가 남아 있을 수 있음
        var thread = new Thread(() =>
        {
            if (_exitEvent.WaitOne() && _services is not null)
                Dispatcher.BeginInvoke(Shutdown);
        })
        {
            IsBackground = true,
            Name = "MoniMS.Exit",
        };
        thread.Start();
    }

    /// <summary>두 번째 실행 시도가 오면 설정 창을 띄운다.</summary>
    private void ListenForActivation()
    {
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        var thread = new Thread(() =>
        {
            while (_activateEvent.WaitOne())
            {
                if (_services is null)
                    return;
                Dispatcher.BeginInvoke(() => _services?.GetRequiredService<WindowService>().ShowManager());
            }
        })
        {
            IsBackground = true,
            Name = "MoniMS.Activation",
        };
        thread.Start();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "UI exception");
        MessageBox.Show($"Something went wrong:\n{e.Exception.Message}\n\nSee the log folder for details.",
            "MoniMS", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("MoniMS stopped");
        _services?.Dispose(); // IDisposable 싱글턴(트레이, 위젯, 모니터) 정리
        _services = null;
        _activateEvent?.Set();
        _activateEvent?.Dispose();
        var exitEvent = _exitEvent;
        _exitEvent = null;
        exitEvent?.Set(); // 대기 중인 스레드를 깨워서 끝냄 (_services가 null이라 아무것도 안 함)
        exitEvent?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
