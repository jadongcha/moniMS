using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using MoniMS.App.Interop;
using MoniMS.App.ViewModels;
using MoniMS.App.Views;
using MoniMS.Core.Desktop;
using MoniMS.Core.Presets;
using MoniMS.Core.Settings;

namespace MoniMS.App.Services;

/// <summary>
/// 위젯 창의 생명주기와 레이아웃을 관리. 프리셋 서비스는 IWidgetLayoutHost로 이 클래스를 사용한다.
/// 현재 레이아웃의 원본은 항상 AppSettings.Widget 이다.
/// </summary>
public sealed class WidgetController : IWidgetLayoutHost, IDisposable
{
    private const double EdgeMargin = 24;
    private readonly WidgetViewModel _vm;
    private readonly ISettingsStore _settings;
    private readonly IThemeService _theme;
    private readonly ILogger<WidgetController> _logger;
    private WidgetWindow? _window;
    private bool _disposed;

    public WidgetController(WidgetViewModel vm, ISettingsStore settings, IThemeService theme, ILogger<WidgetController> logger)
    {
        _vm = vm;
        _settings = settings;
        _theme = theme;
        _logger = logger;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged; // 해상도·배율 변경
    }

    /// <summary>레이아웃이 바뀔 때마다 (트레이 메뉴/설정 창 갱신용).</summary>
    public event EventHandler? LayoutChanged;

    public WidgetLayout Layout => _settings.Current.Widget;

    public bool IsEditMode => _vm.IsEditMode;

    public void Initialize()
    {
        CreateWindow();
        ApplyLayout(Layout);
    }

    public WidgetLayout GetCurrentLayout() => Layout.Clone();

    public void ApplyLayout(WidgetLayout layout)
    {
        layout = layout.Clone();
        layout.UpgradeLegacySize(); // 예전 기본 너비로 저장된 프리셋도 지금 너비로
        _settings.Current.Widget = layout;
        _settings.Save();
        ApplyToWindow();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>현재 레이아웃 일부만 바꿀 때.</summary>
    public void Update(Action<WidgetLayout> change)
    {
        var copy = Layout.Clone();
        change(copy);
        ApplyLayout(copy);
    }

    public void SetEditMode(bool enabled)
    {
        _vm.IsEditMode = enabled;
        ApplyToWindow();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyToWindow()
    {
        if (_window is null)
            return;
        var layout = Layout;

        _vm.ApplyLayout(layout);
        var accent = string.IsNullOrWhiteSpace(layout.AccentColor) ? _theme.GetAccentColor() : SafeParse(layout.AccentColor);
        // 편집 모드에선 완전 투명한 배경도 잡을 수 있도록 살짝 보이게 한다 (투명 픽셀은 마우스가 통과하므로)
        var bgOpacity = _vm.IsEditMode ? Math.Max(layout.Opacity, 0.35) : layout.Opacity;
        _window.ApplyStyle(layout.Style, accent, bgOpacity);
        _window.ApplyFont(layout.FontFamily);

        var firstRun = double.IsNaN(layout.Left) || double.IsNaN(layout.Top);
        FitToMonitor(primary: firstRun);

        if (firstRun)
        {
            // 첫 실행: 주 모니터 오른쪽 위
            var work = SystemParameters.WorkArea;
            _window.Left = work.Right - _vm.WindowWidth - EdgeMargin;
            _window.Top = work.Top + EdgeMargin;
        }
        else
        {
            // 모니터 구성이 바뀌어 화면 밖으로 나가는 경우 방지
            _window.Left = Math.Clamp(layout.Left, SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80);
            _window.Top = Math.Clamp(layout.Top, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80);
        }

        if (!firstRun)
            FitToMonitor(); // 위치를 옮긴 뒤 다른 모니터라면 그 모니터 기준으로 다시

        if (layout.Visible)
        {
            if (!_window.IsVisible)
                _window.Show();
        }
        else
        {
            _window.Hide();
        }

        // 편집 중에는 드래그해야 하므로 클릭 통과를 끈다
        // 항상 클릭 통과. 편집 모드에서만 위젯을 잡고 끌 수 있게 끈다.
        var clickThrough = !_vm.IsEditMode;
        var ex = DesktopPinning.SetClickThrough(_window, clickThrough);
        _logger.LogInformation("Click-through requested={Requested}, applied={Applied} (exStyle=0x{Ex:X})",
            clickThrough, DesktopPinning.IsClickThrough((IntPtr)ex), ex);
    }

    private IntPtr _fittedMonitor;
    private (int, int, double) _fittedMetrics;

    /// <summary>
    /// 해상도가 달라도 화면에서 같은 비율로 보이도록 창 크기를 맞춘다.
    /// (기준: 2880×1800 화면에서 보이는 비율, <see cref="WidgetSizing"/>).
    /// </summary>
    private void FitToMonitor(bool primary = false)
    {
        if (_window is null)
            return;
        var m = MonitorMetrics.ForWindow(_window, primary);
        if (m.Width <= 0)
            return;
        var metrics = (m.Width, m.Height, m.DpiScale);
        _vm.FitToScreen(m.Width, m.Height, m.DpiScale);
        MonitorMetrics.Resize(_window, _vm.WindowWidth, _vm.WindowHeight, m.DpiScale);
        if (m.Handle != _fittedMonitor || metrics != _fittedMetrics)
        {
            _logger.LogInformation("Widget fitted to {W}x{H} @ {Scale:P0}: {WW:F0}x{WH:F0} DIP, content x{CS:F2}",
                m.Width, m.Height, m.DpiScale, _vm.WindowWidth, _vm.WindowHeight, _vm.ContentScale);
            _fittedMonitor = m.Handle;
            _fittedMetrics = metrics;
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        var w = _window;
        if (w is null)
            return;
        w.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => FitToMonitor());
        // 해상도·배율 변경 직후에는 Windows/WPF가 창 크기를 한 번 더 조정하므로 잠시 뒤 다시 맞춘다.
        var timer = new DispatcherTimer(DispatcherPriority.Background, w.Dispatcher) { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            FitToMonitor();
        };
        timer.Start();
    }

    private void CreateWindow()
    {
        var window = new WidgetWindow(_vm);
        window.DpiChanged += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => FitToMonitor());
        window.SourceInitialized += (_, _) => DesktopPinning.Pin(window, OnExplorerRestarted);
        window.MovedByUser += (_, _) =>
        {
            _settings.Current.Widget.Left = window.Left;
            _settings.Current.Widget.Top = window.Top;
            _settings.Save();
            FitToMonitor(); // 다른 모니터로 옮겼을 수 있음
        };
        window.Closed += OnWindowClosed;
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        // 투명(레이어드) 창은 GPU로 그려도 매번 CPU로 읽어 와야 해서 이점이 없고, GPU 드라이버만 메모리에 올라간다.
        // 1초에 한 번 바뀌는 위젯이라 소프트웨어 렌더링이 더 가볍다 (첫 렌더링 전에 지정해야 장치가 안 만들어짐).
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.RenderMode = RenderMode.SoftwareOnly;
        _window = window;
    }

    /// <summary>
    /// 소유자인 바탕화면 창이 사라지면(explorer 재시작) 위젯 창도 함께 파괴된다. 잠시 뒤 다시 만든다.
    /// </summary>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _window = null;
        if (_disposed)
            return;
        _logger.LogInformation("Widget window was closed; recreating (explorer restart?)");
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_disposed || _window is not null)
                return;
            CreateWindow();
            ApplyToWindow();
        };
        timer.Start();
    }

    private void OnExplorerRestarted()
    {
        if (_window is null)
            return;
        // 창이 살아남았다면 새 Progman에 다시 붙인다
        var w = _window;
        w.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            w.Closed -= OnWindowClosed;
            w.Close();
            _window = null;
            CreateWindow();
            ApplyToWindow();
        });
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Windows 강조색이 바뀌면 위젯 색도 따라감
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            _window?.Dispatcher.BeginInvoke(ApplyToWindow);
    }

    private static Rgb SafeParse(string hex)
    {
        try
        {
            return Rgb.Parse(hex);
        }
        catch (FormatException)
        {
            return Rgb.Parse("#60A5FA");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _window?.Close();
    }
}
