using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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
/// 음악 위젯 창의 생명주기와 레이아웃. 현재 레이아웃의 원본은 AppSettings.MediaWidget.
/// 숨겨 두면 창도 띄우지 않고 미디어 세션도 지켜보지 않는다.
/// </summary>
public sealed class MediaWidgetController : IMediaWidgetHost, IDisposable
{
    private readonly ISettingsStore _settings;
    private readonly IThemeService _theme;
    private readonly MediaWidgetViewModel _vm;
    private readonly ILogger<MediaWidgetController> _logger;
    private MediaWidgetWindow? _window;
    private bool _disposed;

    public MediaWidgetController(ISettingsStore settings, IThemeService theme, MediaWidgetViewModel vm, ILogger<MediaWidgetController> logger)
    {
        _settings = settings;
        _theme = theme;
        _vm = vm;
        _logger = logger;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>레이아웃이나 상태가 바뀔 때마다 (트레이 메뉴/설정 창 갱신용).</summary>
    public event EventHandler? LayoutChanged;

    public MediaWidgetLayout Layout => _settings.Current.MediaWidget;

    public bool IsEditMode { get; private set; }

    public void Initialize() => ApplyToWindow();

    public MediaWidgetLayout GetCurrentLayout() => Layout.Clone();

    public void ApplyLayout(MediaWidgetLayout layout) => Commit(layout.Clone());

    /// <summary>현재 레이아웃 일부만 바꿀 때.</summary>
    public void Update(Action<MediaWidgetLayout> change)
    {
        var copy = Layout.Clone();
        change(copy);
        Commit(copy);
    }

    public void SetEditMode(bool enabled)
    {
        IsEditMode = enabled;
        ApplyToWindow();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Commit(MediaWidgetLayout layout)
    {
        _settings.Current.MediaWidget = layout;
        _settings.Save();
        ApplyToWindow();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyToWindow()
    {
        var layout = Layout;
        if (!layout.Visible)
        {
            _window?.Hide();
            _vm.SetActive(false);
            return;
        }

        if (_window is null)
            CreateWindow();
        var w = _window!;
        w.Width = Math.Max(MediaWidgetLayout.MinWidth, layout.Width);
        w.Height = Math.Max(MediaWidgetLayout.MinHeight, layout.Height);
        if (double.IsNaN(layout.Left) || double.IsNaN(layout.Top))
        {
            // 처음: 주 모니터 오른쪽 아래 (정보 위젯은 오른쪽 위에 있으므로)
            var work = SystemParameters.WorkArea;
            layout.Left = work.Right - w.Width - 24;
            layout.Top = work.Bottom - w.Height - 24;
            _settings.Save();
        }
        // 모니터 구성이 바뀌어 화면 밖으로 나가는 경우 방지
        w.Left = Math.Clamp(layout.Left, SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80);
        w.Top = Math.Clamp(layout.Top, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80);

        var accent = ColorPickerViewModel.IsValidHex(layout.AccentColor) ? Rgb.Parse(layout.AccentColor!) : _theme.GetAccentColor();
        w.ApplyStyle(layout.Style, Color.FromRgb(accent.R, accent.G, accent.B), layout.Opacity);
        w.SetEditMode(IsEditMode);
        _vm.SetVolumeTarget(layout.VolumeTarget);
        if (!w.IsVisible)
            w.Show();
        _vm.SetActive(true);
    }

    private void CreateWindow()
    {
        var window = new MediaWidgetWindow(_vm);
        // 버튼을 눌러야 하므로 클릭 통과는 아니지만, 키보드는 쓰지 않으므로 눌러도 활성화되지 않는다
        window.SourceInitialized += (_, _) => DesktopPinning.Pin(window, OnExplorerRestarted);
        window.MovedOrResized += (_, _) =>
        {
            var l = Layout;
            l.Left = window.Left;
            l.Top = window.Top;
            l.Width = Math.Max(MediaWidgetLayout.MinWidth, window.ActualWidth);
            l.Height = Math.Max(MediaWidgetLayout.MinHeight, window.ActualHeight);
            _settings.Save();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        };
        window.Closed += OnWindowClosed;
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        _vm.IsCovered = () => window.IsVisible && Occlusion.IsCovered(hwnd);
        // 투명(레이어드) 창은 GPU로 그려도 매번 CPU로 읽어 와야 해서 이점이 없다 (다른 위젯과 같은 이유)
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.RenderMode = RenderMode.SoftwareOnly;
        _window = window;
    }

    /// <summary>소유자인 바탕화면 창이 사라지면(explorer 재시작) 창도 파괴된다. 잠시 뒤 다시 만든다.</summary>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _window = null;
        _vm.SetActive(false);
        if (_disposed)
            return;
        // 앱을 끌 때도 창이 먼저 닫히므로 여기서는 기록하지 않는다 (그때는 이 타이머가 돌지 않는다)
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_disposed || _window is not null)
                return;
            _logger.LogInformation("Media widget window was closed; recreating (explorer restart?)");
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
            ApplyToWindow();
        });
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Windows 강조색이 바뀌면 따라간다
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, ApplyToWindow);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, ApplyToWindow);

    public void Dispose()
    {
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _window?.Close();
        _vm.Dispose();
    }
}
