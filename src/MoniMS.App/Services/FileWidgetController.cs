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
using MoniMS.Core.Files;
using MoniMS.Core.Presets;
using MoniMS.Core.Settings;

namespace MoniMS.App.Services;

/// <summary>
/// 파일 위젯 창의 생명주기와 레이아웃. 현재 레이아웃의 원본은 AppSettings.FileWidget.
/// 폴더를 하나도 고르지 않았으면 창을 띄우지 않는다.
/// </summary>
public sealed class FileWidgetController : IFileWidgetHost, IDisposable
{
    private readonly ISettingsStore _settings;
    private readonly IThemeService _theme;
    private readonly FileWidgetViewModel _vm;
    private readonly ILogger<FileWidgetController> _logger;
    private FileWidgetWindow? _window;
    private bool _disposed;

    /// <summary>폴더를 옮겨 다닐 때마다 설정 파일을 쓰지 않도록 잠깐 모았다가 한 번에 저장한다.</summary>
    private readonly DispatcherTimer _saveTimer;

    public FileWidgetController(ISettingsStore settings, IThemeService theme, FileWidgetViewModel vm, ILogger<FileWidgetController> logger)
    {
        _settings = settings;
        _theme = theme;
        _vm = vm;
        _logger = logger;
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _saveTimer.Tick += (_, _) => SaveNow();
        _vm.StateChanged += OnViewStateChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>레이아웃이나 상태가 바뀔 때마다 (트레이 메뉴/설정 창 갱신용).</summary>
    public event EventHandler? LayoutChanged;

    public FileWidgetLayout Layout => _settings.Current.FileWidget;

    public bool IsEditMode { get; private set; }

    public void Initialize() => ApplyToWindow();

    public FileWidgetLayout GetCurrentLayout() => Layout.Clone();

    public void ApplyLayout(FileWidgetLayout layout) => Commit(layout.Clone());

    /// <summary>현재 레이아웃 일부만 바꿀 때.</summary>
    public void Update(Action<FileWidgetLayout> change)
    {
        var copy = Layout.Clone();
        change(copy);
        Commit(copy);
    }

    /// <summary>폴더를 왼쪽 목록에 더한다 (이미 있으면 건너뜀). 처음 고른 폴더면 위젯을 보이게 하고 그 폴더를 연다.</summary>
    public void AddFolders(IEnumerable<string> folders) => Update(l =>
    {
        var first = !l.HasFolders;
        foreach (var folder in folders.Select(FileTransferRules.Normalize))
        {
            if (!l.Folders.Any(f => FileTransferRules.PathsEqual(f, folder)))
                l.Folders.Add(folder);
        }
        if (first && l.HasFolders)
        {
            l.Visible = true;
            l.CurrentFolder = l.Folders[0];
        }
    });

    public void RemoveFolder(string folder) => Update(l => l.Folders.RemoveAll(f => FileTransferRules.PathsEqual(f, folder)));

    public bool ContainsFolder(string folder) => Layout.Folders.Any(f => FileTransferRules.PathsEqual(f, folder));

    public void SetEditMode(bool enabled)
    {
        IsEditMode = enabled;
        ApplyToWindow();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Commit(FileWidgetLayout layout)
    {
        _settings.Current.FileWidget = layout;
        _settings.Save();
        ApplyToWindow();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyToWindow()
    {
        var layout = Layout;
        if (!layout.HasFolders || !layout.Visible)
        {
            _window?.Hide();
            _vm.SetActive(false);
            return;
        }

        if (_window is null)
            CreateWindow();
        var w = _window!;
        w.Width = Math.Max(FileWidgetLayout.MinWidth, layout.Width);
        w.Height = Math.Max(FileWidgetLayout.MinHeight, layout.Height);
        if (double.IsNaN(layout.Left) || double.IsNaN(layout.Top))
        {
            // 처음: 주 모니터 가운데보다 조금 왼쪽 위 (정보 위젯은 오른쪽 위, 아이콘은 왼쪽 끝에 있으므로)
            var work = SystemParameters.WorkArea;
            layout.Left = work.Left + Math.Max(0, work.Width - w.Width) * 0.3;
            layout.Top = work.Top + Math.Max(0, work.Height - w.Height) * 0.3;
            _settings.Save();
        }
        // 모니터 구성이 바뀌어 화면 밖으로 나가는 경우 방지
        w.Left = Math.Clamp(layout.Left, SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80);
        w.Top = Math.Clamp(layout.Top, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80);

        var accent = _theme.GetAccentColor();
        w.ApplyStyle(layout.Style, Color.FromRgb(accent.R, accent.G, accent.B), layout.Opacity);
        w.SetEditMode(IsEditMode);
        _vm.ApplyView(layout.SortBy, layout.SortDescending, layout.ExtensionColumnWidth);
        _vm.SetFolders(layout.Folders, layout.CurrentFolder);
        if (!w.IsVisible)
            w.Show();
        _vm.SetActive(true);
    }

    /// <summary>위젯 안에서 연 폴더·정렬·열 너비를 기억한다 (다음 실행 때 그대로). 저장은 잠시 뒤 한 번에.</summary>
    private void OnViewStateChanged(object? sender, EventArgs e)
    {
        var l = Layout;
        l.CurrentFolder = _vm.CurrentPath;
        l.SortBy = _vm.SortBy;
        l.SortDescending = _vm.SortDescending;
        l.ExtensionColumnWidth = _vm.ExtensionColumnWidth;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        _settings.Save();
    }

    private void CreateWindow()
    {
        var window = new FileWidgetWindow(_vm, this);
        // 클릭하면 활성화돼야 키보드(Enter, Delete, F2, Ctrl+C …)를 받는다. 그래도 항상 맨 아래에 있다.
        window.SourceInitialized += (_, _) => DesktopPinning.Pin(window, OnExplorerRestarted, activatable: true);
        window.MovedOrResized += (_, _) =>
        {
            var l = Layout;
            l.Left = window.Left;
            l.Top = window.Top;
            l.Width = Math.Max(FileWidgetLayout.MinWidth, window.ActualWidth);
            l.Height = Math.Max(FileWidgetLayout.MinHeight, window.ActualHeight);
            _settings.Save();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        };
        window.Closed += OnWindowClosed;
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        WidgetSnapping.Attach(window); // 끌어 옮길 때 다른 위젯·화면 가장자리에 가지런히 붙는다
        _vm.OwnerHandle = hwnd;
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
            _logger.LogInformation("File widget window was closed; recreating (explorer restart?)");
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
        // Windows 강조색(선택 색)이 바뀌면 따라간다
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, ApplyToWindow);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, ApplyToWindow);

    public void Dispose()
    {
        _disposed = true;
        if (_saveTimer.IsEnabled)
            SaveNow(); // 아직 저장하지 않은 폴더 이동
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _vm.StateChanged -= OnViewStateChanged;
        _window?.Close();
        _vm.Dispose();
    }
}
