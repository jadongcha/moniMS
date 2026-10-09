using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using MoniMS.App.Imaging;
using MoniMS.App.Interop;
using MoniMS.App.Views;
using MoniMS.Core.Desktop;
using MoniMS.Core.Presets;
using MoniMS.Core.Settings;

namespace MoniMS.App.Services;

/// <summary>
/// 사진/GIF 위젯 창의 생명주기와 레이아웃. 현재 레이아웃의 원본은 AppSettings.ImageWidget이고,
/// 이미지 파일은 ImageStore(%APPDATA%\MoniMS\images)에 복사해 둔 것을 쓴다.
/// </summary>
public sealed class ImageWidgetController : IImageWidgetHost, IDisposable
{
    private readonly ISettingsStore _settings;
    private readonly IThemeService _theme;
    private readonly ImageStore _store;
    private readonly ILogger<ImageWidgetController> _logger;
    private ImageWidgetWindow? _window;
    private string? _loadedPath;
    private int _loadVersion;
    private bool _disposed;

    public ImageWidgetController(ISettingsStore settings, IThemeService theme, ImageStore store, ILogger<ImageWidgetController> logger)
    {
        _settings = settings;
        _theme = theme;
        _store = store;
        _logger = logger;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>레이아웃이나 상태가 바뀔 때마다 (트레이 메뉴/설정 창 갱신용).</summary>
    public event EventHandler? LayoutChanged;

    public ImageWidgetLayout Layout => _settings.Current.ImageWidget;

    public bool IsEditMode { get; private set; }

    /// <summary>마지막으로 이미지를 열지 못한 이유 (설정 창 표시용). 정상이면 null.</summary>
    public string? Error { get; private set; }

    public void Initialize()
    {
        if (Layout.ImagePath is { } path && !File.Exists(path))
        {
            _logger.LogWarning("Image widget image is missing: {Path}", path);
            Layout.ImagePath = null;
            _settings.Save();
        }
        ApplyToWindow();
    }

    public ImageWidgetLayout GetCurrentLayout() => Layout.Clone();

    /// <summary>프리셋 적용: 이미지(프리셋 폴더 안의 파일)는 보관함으로 복사해서 쓴다 (프리셋을 지워도 남도록).</summary>
    public void ApplyLayout(ImageWidgetLayout layout)
    {
        var copy = layout.Clone();
        if (copy.ImagePath is { } path)
            copy.ImagePath = File.Exists(path) ? _store.Import(path) : null;
        Commit(copy);
    }

    /// <summary>현재 레이아웃 일부만 바꿀 때.</summary>
    public void Update(Action<ImageWidgetLayout> change)
    {
        var copy = Layout.Clone();
        change(copy);
        Commit(copy);
    }

    /// <summary>새 이미지를 고른다: 보관함으로 복사하고 창을 이미지 비율에 맞춘다. 열 수 없는 파일이면 false.</summary>
    public bool SetImage(string file)
    {
        if (ImageLoader.ReadSize(file) is not { } size || size.Width <= 0 || size.Height <= 0)
        {
            Error = $"'{Path.GetFileName(file)}' is not an image MoniMS can open.";
            LayoutChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }
        var stored = _store.Import(file);
        Update(l =>
        {
            l.ImagePath = stored;
            l.ImageName = Path.GetFileName(file);
            l.Visible = true;
            FitSize(l, (double)size.Width / size.Height, SystemParameters.WorkArea);
        });
        return true;
    }

    public void RemoveImage() => Update(l =>
    {
        l.ImagePath = null;
        l.ImageName = null;
    });

    /// <summary>너비를 바꾸고 높이는 이미지 비율대로.</summary>
    public void SetWidth(double width) => Update(l =>
    {
        var aspect = l.Height > 0 ? l.Width / l.Height : 1;
        l.Width = Math.Max(ImageWidgetLayout.MinSize, width);
        l.Height = Math.Max(ImageWidgetLayout.MinSize, l.Width / aspect);
    });

    public void SetEditMode(bool enabled)
    {
        IsEditMode = enabled;
        ApplyToWindow();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 이미지 비율에 맞는 창 크기. 지금 너비를 유지하되 화면의 60%를 넘지 않게 한다.
    /// </summary>
    internal static void FitSize(ImageWidgetLayout l, double aspect, Rect workArea)
    {
        var width = l.Width > 0 ? l.Width : ImageWidgetLayout.DefaultWidth;
        var height = width / aspect;
        if (height > workArea.Height * 0.6)
        {
            height = workArea.Height * 0.6;
            width = height * aspect;
        }
        if (width > workArea.Width * 0.6)
        {
            width = workArea.Width * 0.6;
            height = width / aspect;
        }
        var grow = Math.Max(1, ImageWidgetLayout.MinSize / Math.Min(width, height));
        l.Width = width * grow;
        l.Height = height * grow;
    }

    private void Commit(ImageWidgetLayout layout)
    {
        var imageChanged = !string.Equals(layout.ImagePath, Layout.ImagePath, StringComparison.OrdinalIgnoreCase);
        _settings.Current.ImageWidget = layout;
        _settings.Save();
        if (imageChanged)
        {
            Error = null;
            _store.RemoveUnused(layout.ImagePath); // 예전 이미지 복사본 정리 (프리셋에는 따로 복사본이 있다)
        }
        ApplyToWindow();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyToWindow()
    {
        var layout = Layout;
        if (!layout.HasImage || !layout.Visible)
        {
            _window?.Hide();
            if (!layout.HasImage && _window is not null)
            {
                _window.ShowImage(null); // 메모리 해제
                _loadedPath = null;
            }
            return;
        }

        if (_window is null)
            CreateWindow();
        var w = _window!;
        w.Width = Math.Max(ImageWidgetLayout.MinSize, layout.Width);
        w.Height = Math.Max(ImageWidgetLayout.MinSize, layout.Height);

        if (double.IsNaN(layout.Left) || double.IsNaN(layout.Top))
        {
            // 처음: 주 모니터 가운데
            var work = SystemParameters.WorkArea;
            layout.Left = work.Left + (work.Width - w.Width) / 2;
            layout.Top = work.Top + (work.Height - w.Height) / 2;
            _settings.Save();
        }
        // 모니터 구성이 바뀌어 화면 밖으로 나가는 경우 방지
        w.Left = Math.Clamp(layout.Left, SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 48);
        w.Top = Math.Clamp(layout.Top, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 48);

        var accent = _theme.GetAccentColor();
        w.ApplyAppearance(layout.Opacity, layout.CornerRadius);
        w.SetEditMode(IsEditMode, Color.FromRgb(accent.R, accent.G, accent.B));
        if (!w.IsVisible)
            w.Show();
        DesktopPinning.SetClickThrough(w, !IsEditMode); // 편집 모드에서만 잡을 수 있다

        if (!string.Equals(_loadedPath, layout.ImagePath, StringComparison.OrdinalIgnoreCase))
            _ = LoadAsync(layout.ImagePath!);
    }

    /// <summary>이미지 디코딩(큰 GIF는 수백 ms)은 백그라운드에서. 그사이 다른 이미지로 바뀌면 버린다.</summary>
    private async Task LoadAsync(string path)
    {
        _loadedPath = path;
        var version = ++_loadVersion;
        var image = await ImageLoader.LoadAsync(path);
        if (version != _loadVersion || _window is null)
            return;

        if (image is null)
        {
            Error = $"Could not open '{Layout.ImageName ?? Path.GetFileName(path)}'. It may be damaged or in an unsupported format.";
            _logger.LogWarning("Image widget could not open {Path}", path);
            _loadedPath = null;
            _window.ShowImage(null);
            _window.Hide();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _window.ShowImage(image);
        _logger.LogInformation("Image widget showing {Name}: {W}x{H}, {Frames} frame(s)",
            Layout.ImageName, image.First.PixelWidth, image.First.PixelHeight, image.Frames.Count);

        // 저장된 크기가 이미지 비율과 다르면 (예: 이미지만 바뀐 설정 파일) 높이를 맞춘다
        var height = Layout.Width / image.AspectRatio;
        if (Math.Abs(height - Layout.Height) > 1)
        {
            Layout.Height = height;
            _settings.Save();
            _window.Height = height;
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CreateWindow()
    {
        var window = new ImageWidgetWindow();
        window.SourceInitialized += (_, _) => DesktopPinning.Pin(window, OnExplorerRestarted);
        window.MovedOrResized += (_, _) =>
        {
            var l = Layout;
            l.Left = window.Left;
            l.Top = window.Top;
            l.Width = Math.Max(ImageWidgetLayout.MinSize, window.ActualWidth);
            l.Height = Math.Max(ImageWidgetLayout.MinSize, window.ActualHeight);
            _settings.Save();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        };
        window.Closed += OnWindowClosed;
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        // 투명(레이어드) 창은 GPU로 그려도 매번 CPU로 읽어 와야 해서 이점이 없고 GPU 드라이버 메모리만 늘어난다
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.RenderMode = RenderMode.SoftwareOnly;
        _window = window;
        _loadedPath = null;
    }

    /// <summary>소유자인 바탕화면 창이 사라지면(explorer 재시작) 창도 파괴된다. 잠시 뒤 다시 만든다.</summary>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _window = null;
        _loadedPath = null;
        if (_disposed)
            return;
        _logger.LogInformation("Image widget window was closed; recreating (explorer restart?)");
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_disposed && _window is null)
                ApplyToWindow();
        };
        timer.Start();
    }

    private void OnExplorerRestarted()
    {
        if (_window is not { } w)
            return;
        w.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            w.Closed -= OnWindowClosed;
            w.Close();
            _window = null;
            _loadedPath = null;
            ApplyToWindow();
        });
    }

    /// <summary>화면이 잠기면 GIF 재생을 멈춘다 (보이지도 않는데 CPU를 쓰지 않도록).</summary>
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionUnlock)
        {
            var locked = e.Reason == SessionSwitchReason.SessionLock;
            Application.Current?.Dispatcher.BeginInvoke(() => _window?.SetPaused(locked));
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, ApplyToWindow);

    public void Dispose()
    {
        _disposed = true;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _window?.Close();
    }
}
