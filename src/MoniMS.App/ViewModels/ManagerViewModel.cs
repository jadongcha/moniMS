using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoniMS.App.Services;
using MoniMS.Core;
using MoniMS.Core.Presets;
using MoniMS.Core.Settings;

namespace MoniMS.App.ViewModels;

public sealed partial class PresetItemViewModel : ObservableObject
{
    public PresetItemViewModel(Preset preset, bool isActive)
    {
        Preset = preset;
        IsActive = isActive;
    }

    public Preset Preset { get; }
    public string Name => Preset.Name;
    public bool IsActive { get; }

    public string PartsText
    {
        get
        {
            var parts = new List<string>();
            if (Preset.Wallpaper is not null) parts.Add("Wallpaper");
            if (Preset.Theme is not null) parts.Add("Theme");
            if (Preset.Icons is not null) parts.Add("Icon layout");
            if (Preset.Widget is not null) parts.Add("Widget");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>짧은 날짜: 올해면 "10-07 19:51", 작년 이전이면 "25-10-07".</summary>
    public string UpdatedText => Preset.UpdatedAt.Year == DateTimeOffset.Now.Year
        ? Preset.UpdatedAt.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)
        : Preset.UpdatedAt.ToString("yy-MM-dd", CultureInfo.InvariantCulture);
}

public sealed partial class SectionToggleViewModel(WidgetSection kind, string displayName, bool isEnabled) : ObservableObject
{
    public WidgetSection Kind { get; } = kind;
    public string DisplayName { get; } = displayName;

    [ObservableProperty] private bool _isEnabled = isEnabled;
}

public sealed partial class ManagerViewModel : ObservableObject
{
    private static readonly Dictionary<WidgetSection, string> SectionNames = new()
    {
        [WidgetSection.System] = "System info (OS · CPU · GPU · RAM · board)",
        [WidgetSection.Cpu] = "CPU usage",
        [WidgetSection.Memory] = "Memory usage",
        [WidgetSection.Gpu] = "GPU usage",
        [WidgetSection.Disk] = "Drives",
        [WidgetSection.Network] = "Network (IP · speed)",
        [WidgetSection.Uptime] = "Uptime · clock",
    };

    private readonly PresetService _presets;
    private readonly WidgetController _widget;
    private readonly TrayIconService _tray;
    private readonly ISettingsStore _settings;
    private bool _loadingWidget;

    private readonly UpdateService _updates;
    private Velopack.UpdateInfo? _pendingUpdate;

    public ManagerViewModel(PresetService presets, WidgetController widget, TrayIconService tray, ISettingsStore settings,
        ShellThemesViewModel shell, UpdateService updates)
    {
        Shell = shell;
        _updates = updates;
        _updateStatus = updates.IsSupported
            ? "Updates are downloaded from GitHub Releases."
            : "Automatic updates work when MoniMS is installed with the setup program.";
        _presets = presets;
        _widget = widget;
        _tray = tray;
        _settings = settings;
        _startWithWindows = StartupRegistration.IsEnabled;
        _isDarkMode = settings.Current.UiTheme == UiTheme.Dark;
        ReloadPresets();
        LoadWidget();
        _widget.LayoutChanged += OnWidgetLayoutChanged;
    }

    /// <summary>창이 닫힐 때 호출 (이벤트 구독 해제).</summary>
    public void Detach()
    {
        _widget.LayoutChanged -= OnWidgetLayoutChanged;
        Shell.Dispose();
    }

    /// <summary>트레이 메뉴 등 다른 곳에서 바뀐 표시 상태를 체크박스에 반영.</summary>
    private void OnWidgetLayoutChanged(object? sender, EventArgs e)
    {
        if (_loadingWidget)
            return;
        _loadingWidget = true;
        WidgetVisible = _widget.Layout.Visible;
        EditMode = _widget.IsEditMode;
        _loadingWidget = false;
    }

    /// <summary>"Themes" 탭.</summary>
    public ShellThemesViewModel Shell { get; }

    // ================= 프리셋 =================
    public ObservableCollection<PresetItemViewModel> Presets { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(OverwriteCommand), nameof(RenameCommand), nameof(DeleteCommand))]
    private PresetItemViewModel? _selectedPreset;

    /// <summary>오른쪽 패널의 미리보기. null이면 "현재 바탕화면 저장" 폼을 보여준다.</summary>
    [ObservableProperty] private PresetPreviewViewModel? _preview;

    partial void OnSelectedPresetChanged(PresetItemViewModel? value)
    {
        Preview = value is null ? null : PresetPreviewViewModel.Create(value.Preset, _presets.GetDirectory(value.Preset));
    }

    /// <summary>선택을 풀어서 저장 폼으로 돌아감.</summary>
    [RelayCommand]
    private void NewPreset() => SelectedPreset = null;

    [ObservableProperty] private string _newPresetName = "";
    [ObservableProperty] private bool _includeWallpaper = true;
    [ObservableProperty] private bool _includeTheme = true;
    [ObservableProperty] private bool _includeIcons = true;
    [ObservableProperty] private bool _includeWidget = true;
    [ObservableProperty] private string _status = "";

    private PresetParts SelectedParts =>
        (IncludeWallpaper ? PresetParts.Wallpaper : 0) | (IncludeTheme ? PresetParts.Theme : 0)
        | (IncludeIcons ? PresetParts.Icons : 0) | (IncludeWidget ? PresetParts.Widget : 0);

    private void ReloadPresets(string? selectId = null)
    {
        selectId ??= SelectedPreset?.Preset.Id;
        Presets.Clear();
        foreach (var p in _presets.GetAll())
            Presets.Add(new PresetItemViewModel(p, p.Id == _settings.Current.LastAppliedPresetId));
        SelectedPreset = Presets.FirstOrDefault(p => p.Preset.Id == selectId);
    }

    [RelayCommand]
    private void SaveNew()
    {
        if (SelectedParts == PresetParts.None)
        {
            Status = "Select at least one item to save.";
            return;
        }
        var name = string.IsNullOrWhiteSpace(NewPresetName) ? $"Preset {DateTime.Now:MM-dd HH:mm}" : NewPresetName.Trim();
        var preset = Run(() => _presets.Capture(name, SelectedParts));
        if (preset is null)
            return;
        NewPresetName = "";
        ReloadPresets(preset.Id);
        Status = $"Saved '{name}'";
    }

    private bool HasSelection() => SelectedPreset is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Apply()
    {
        var preset = SelectedPreset!.Preset;
        Run(() => _tray.ApplyPreset(preset));
        LoadWidget();
        ReloadPresets();
        Status = $"Applied '{preset.Name}'";
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Overwrite()
    {
        var preset = SelectedPreset!.Preset;
        // 프리셋에 원래 들어 있던 항목만 덮어쓴다 (저장 폼은 선택 중엔 숨겨져 있으므로)
        var parts = preset.AvailableParts == PresetParts.None ? PresetParts.All : preset.AvailableParts;
        if (MessageBox.Show($"Overwrite '{preset.Name}' with the current desktop?", "MoniMS",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        Run(() => _presets.Overwrite(preset, parts));
        ReloadPresets();
        Status = $"Overwrote '{preset.Name}'";
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Rename()
    {
        var preset = SelectedPreset!.Preset;
        var newName = Views.InputDialog.Ask("Rename preset", "New name", preset.Name);
        if (string.IsNullOrWhiteSpace(newName))
            return;
        Run(() => _presets.Rename(preset, newName.Trim()));
        ReloadPresets();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        var preset = SelectedPreset!.Preset;
        if (MessageBox.Show($"Delete preset '{preset.Name}'?\nIts saved wallpaper copies will be deleted too.", "MoniMS",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        Run(() => _presets.Delete(preset));
        ReloadPresets();
    }

    [RelayCommand]
    private static void OpenDataFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDirectory}\"") { UseShellExecute = true });

    // ================= 위젯 =================
    public IReadOnlyList<WidgetStyle> Styles { get; } = Enum.GetValues<WidgetStyle>();

    public const string BuiltInFont = "JetBrains Mono (built-in)";

    /// <summary>내장 글꼴 + 설치된 시스템 글꼴.</summary>
    public IReadOnlyList<string> FontChoices { get; } =
        [BuiltInFont, .. System.Windows.Media.Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().Order()];

    [ObservableProperty] private string _font = BuiltInFont;

    public ObservableCollection<SectionToggleViewModel> Sections { get; } = [];

    [ObservableProperty] private SectionToggleViewModel? _selectedSection;
    [ObservableProperty] private bool _widgetVisible;
    [ObservableProperty] private bool _editMode;
    [ObservableProperty] private WidgetStyle _style;
    [ObservableProperty] private double _widgetOpacity;
    [ObservableProperty] private double _widgetWidth;
    [ObservableProperty] private double _widgetHeight;
    [ObservableProperty] private double _widgetScale;
    [ObservableProperty] private string _accentColor = "";

    private void LoadWidget()
    {
        _loadingWidget = true;
        var l = _widget.Layout;
        WidgetVisible = l.Visible;
        EditMode = _widget.IsEditMode;
        Style = l.Style;
        WidgetOpacity = l.Opacity;
        WidgetWidth = l.Width;
        WidgetHeight = l.Height;
        WidgetScale = l.Scale;
        AccentColor = l.AccentColor ?? "";
        Font = string.IsNullOrWhiteSpace(l.FontFamily) ? BuiltInFont : l.FontFamily;

        Sections.Clear();
        foreach (var kind in l.Sections)
            Sections.Add(CreateToggle(kind, true));
        foreach (var kind in Enum.GetValues<WidgetSection>().Except(l.Sections))
            Sections.Add(CreateToggle(kind, false));
        _loadingWidget = false;
    }

    private SectionToggleViewModel CreateToggle(WidgetSection kind, bool enabled)
    {
        var vm = new SectionToggleViewModel(kind, SectionNames[kind], enabled);
        vm.PropertyChanged += (_, _) => PushWidget();
        return vm;
    }

    partial void OnWidgetVisibleChanged(bool value) => PushWidget();
    partial void OnStyleChanged(WidgetStyle value) => PushWidget();
    partial void OnWidgetOpacityChanged(double value) => PushWidget();
    partial void OnWidgetWidthChanged(double value) => PushWidget();
    partial void OnWidgetHeightChanged(double value) => PushWidget();
    partial void OnWidgetScaleChanged(double value) => PushWidget();
    partial void OnAccentColorChanged(string value) => PushWidget();
    partial void OnFontChanged(string value) => PushWidget();

    partial void OnEditModeChanged(bool value)
    {
        if (!_loadingWidget)
            _widget.SetEditMode(value);
    }

    [RelayCommand]
    private void MoveSectionUp() => MoveSection(-1);

    [RelayCommand]
    private void MoveSectionDown() => MoveSection(+1);

    private void MoveSection(int delta)
    {
        if (SelectedSection is null)
            return;
        var i = Sections.IndexOf(SelectedSection);
        var j = i + delta;
        if (j < 0 || j >= Sections.Count)
            return;
        Sections.Move(i, j);
        PushWidget();
    }

    /// <summary>설정 창에서 바꾼 값을 즉시 위젯에 반영 (위치는 유지).</summary>
    private void PushWidget()
    {
        if (_loadingWidget)
            return;
        _widget.Update(l =>
        {
            l.Visible = WidgetVisible;
            l.Style = Style;
            l.Opacity = WidgetOpacity;
            l.Width = WidgetWidth;
            l.Height = WidgetHeight;
            l.Scale = WidgetScale;
            l.AccentColor = IsValidHex(AccentColor) ? AccentColor.Trim() : null;
            l.FontFamily = string.IsNullOrWhiteSpace(Font) || Font == BuiltInFont ? null : Font.Trim();
            l.Sections = Sections.Where(s => s.IsEnabled).Select(s => s.Kind).ToList();
        });
    }

    private static bool IsValidHex(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return false;
        var t = s.Trim().TrimStart('#');
        return t.Length == 6 && t.All(Uri.IsHexDigit);
    }

    // ================= 테마 =================
    [ObservableProperty] private bool _isDarkMode;

    partial void OnIsDarkModeChanged(bool value)
    {
        var theme = value ? UiTheme.Dark : UiTheme.Light;
        UiThemeManager.Apply(theme);
        _settings.Current.UiTheme = theme;
        _settings.Save();
    }

    // ================= 일반 =================
    [ObservableProperty] private bool _startWithWindows;

    public string DataDirectory => AppPaths.DataDirectory;

    public string Version => _updates.CurrentVersion;

    // ================= 업데이트 =================
    public bool UpdatesSupported => _updates.IsSupported;

    [ObservableProperty] private string _updateStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdatesCommand), nameof(InstallUpdateCommand))]
    private bool _isUpdating;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool _updateAvailable;

    private bool CanCheckUpdates() => UpdatesSupported && !IsUpdating;

    [RelayCommand(CanExecute = nameof(CanCheckUpdates))]
    private async Task CheckUpdatesAsync()
    {
        IsUpdating = true;
        UpdateStatus = "Checking...";
        try
        {
            _pendingUpdate = await _updates.CheckAsync();
            UpdateAvailable = _pendingUpdate is not null;
            UpdateStatus = _pendingUpdate is null
                ? $"You have the latest version ({Version})."
                : $"Version {_pendingUpdate.TargetFullRelease.Version} is available.";
        }
        catch (Exception ex)
        {
            UpdateStatus = "Update check failed: " + ex.Message;
        }
        finally
        {
            IsUpdating = false;
        }
    }

    private bool CanInstallUpdate() => UpdateAvailable && !IsUpdating;

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdate is null)
            return;
        IsUpdating = true;
        try
        {
            await _updates.DownloadAsync(_pendingUpdate, p => Application.Current.Dispatcher.BeginInvoke(() => UpdateStatus = $"Downloading... {p}%"));
            UpdateStatus = "Restarting to finish the update...";
            _settings.Save();
            _tray.Dispose(); // 종료 후 트레이에 아이콘이 남지 않도록
            _updates.ApplyAndRestart(_pendingUpdate); // 앱이 바로 종료된다
        }
        catch (Exception ex)
        {
            UpdateStatus = "Update failed: " + ex.Message;
            IsUpdating = false;
        }
    }

    partial void OnStartWithWindowsChanged(bool value) =>
        Run(() => StartupRegistration.Set(value, Environment.ProcessPath!));

    private T? Run<T>(Func<T> action) where T : class
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            Status = "Error: " + ex.Message;
            return null;
        }
    }

    private void Run(Action action) => Run<object>(() =>
    {
        action();
        return new object();
    });
}
