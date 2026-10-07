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
/// ?꾩젽 李쎌쓽 ?앸챸二쇨린? ?덉씠?꾩썐??愿由? ?꾨━???쒕퉬?ㅻ뒗 IWidgetLayoutHost濡????대옒?ㅻ? ?ъ슜?쒕떎.
/// ?꾩옱 ?덉씠?꾩썐???먮낯? ??긽 AppSettings.Widget ?대떎.
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
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged; // ?댁긽?꽷룸같??蹂寃?
    }

    /// <summary>?덉씠?꾩썐??諛붾??뚮쭏??(?몃젅??硫붾돱/?ㅼ젙 李?媛깆떊??.</summary>
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
        layout.UpgradeLegacySize(); // ?덉쟾 湲곕낯 ?덈퉬濡???λ맂 ?꾨━?뗫룄 吏湲??덈퉬濡?
        _settings.Current.Widget = layout;
        _settings.Save();
        ApplyToWindow();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>?꾩옱 ?덉씠?꾩썐 ?쇰?留?諛붽? ??</summary>
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
        // ?몄쭛 紐⑤뱶?먯꽑 ?꾩쟾 ?щ챸??諛곌꼍???≪쓣 ???덈룄濡??댁쭩 蹂댁씠寃??쒕떎 (?щ챸 ?쎌?? 留덉슦?ㅺ? ?듦낵?섎?濡?
        var bgOpacity = _vm.IsEditMode ? Math.Max(layout.Opacity, 0.35) : layout.Opacity;
        _window.ApplyStyle(layout.Style, accent, bgOpacity);
        _window.ApplyFont(layout.FontFamily);

        var firstRun = double.IsNaN(layout.Left) || double.IsNaN(layout.Top);
        FitToMonitor(primary: firstRun);

        if (firstRun)
        {
            // 泥??ㅽ뻾: 二?紐⑤땲???ㅻⅨ履???
            var work = SystemParameters.WorkArea;
            _window.Left = work.Right - _vm.WindowWidth - EdgeMargin;
            _window.Top = work.Top + EdgeMargin;
        }
        else
        {
            // 紐⑤땲??援ъ꽦??諛붾뚯뼱 ?붾㈃ 諛뽰쑝濡??섍???寃쎌슦 諛⑹?
            _window.Left = Math.Clamp(layout.Left, SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80);
            _window.Top = Math.Clamp(layout.Top, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80);
        }

        if (!firstRun)
            FitToMonitor(); // ?꾩튂瑜???릿 ???ㅻⅨ 紐⑤땲?곕씪硫?洹?紐⑤땲??湲곗??쇰줈 ?ㅼ떆

        if (layout.Visible)
        {
            if (!_window.IsVisible)
                _window.Show();
        }
        else
        {
            _window.Hide();
        }

        // ?몄쭛 以묒뿉???쒕옒洹명빐???섎?濡??대┃ ?듦낵瑜??덈떎
        // ??긽 ?대┃ ?듦낵. ?몄쭛 紐⑤뱶?먯꽌留??꾩젽???↔퀬 ?????덇쾶 ?덈떎.
        var clickThrough = !_vm.IsEditMode;
        var ex = DesktopPinning.SetClickThrough(_window, clickThrough);
        _logger.LogInformation("Click-through requested={Requested}, applied={Applied} (exStyle=0x{Ex:X})",
            clickThrough, DesktopPinning.IsClickThrough((IntPtr)ex), ex);
    }

    private IntPtr _fittedMonitor;
    private (int, int, double) _fittedMetrics;

    /// <summary>
    /// ?댁긽?꾧? ?щ씪???붾㈃?먯꽌 媛숈? 鍮꾩쑉濡?蹂댁씠?꾨줉 李??ш린瑜?留욎텣??
    /// (湲곗?: 2880횞1800 ?붾㈃?먯꽌 蹂댁씠??鍮꾩쑉, <see cref="WidgetSizing"/>).
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
        // ?댁긽?꽷룸같??蹂寃?吏곹썑?먮뒗 Windows/WPF媛 李??ш린瑜???踰???議곗젙?섎?濡??좎떆 ???ㅼ떆 留욎텣??
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
            FitToMonitor(); // ?ㅻⅨ 紐⑤땲?곕줈 ??꼈?????덉쓬
        };
        window.Closed += OnWindowClosed;
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        // ?щ챸(?덉씠?대뱶) 李쎌? GPU濡?洹몃젮??留ㅻ쾲 CPU濡??쎌뼱 ????댁꽌 ?댁젏???녾퀬, GPU ?쒕씪?대쾭留?硫붾え由ъ뿉 ?щ씪媛꾨떎.
        // 1珥덉뿉 ??踰?諛붾뚮뒗 ?꾩젽?대씪 ?뚰봽?몄썾???뚮뜑留곸씠 ??媛蹂띾떎 (泥??뚮뜑留??꾩뿉 吏?뺥빐???μ튂媛 ??留뚮뱾?댁쭚).
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.RenderMode = RenderMode.SoftwareOnly;
        _window = window;
    }

    /// <summary>
    /// ?뚯쑀?먯씤 諛뷀깢?붾㈃ 李쎌씠 ?щ씪吏硫?explorer ?ъ떆?? ?꾩젽 李쎈룄 ?④퍡 ?뚭눼?쒕떎. ?좎떆 ???ㅼ떆 留뚮뱺??
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
        // 李쎌씠 ?댁븘?⑥븯?ㅻ㈃ ??Progman???ㅼ떆 遺숈씤??
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
        // Windows 媛뺤“?됱씠 諛붾뚮㈃ ?꾩젽 ?됰룄 ?곕씪媛?
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
