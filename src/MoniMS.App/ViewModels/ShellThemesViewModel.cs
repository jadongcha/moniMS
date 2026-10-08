using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MoniMS.Core.Settings;
using MoniMS.Core.Shell;
using MoniMS.Core.Shell.Apps;

namespace MoniMS.App.ViewModels;

public sealed class ThemeListItem(ThemePackage package)
{
    public ThemePackage Package { get; } = package;
    public string Name => Package.Name;

    public string PartsText
    {
        get
        {
            var parts = Package.Mods.Select(m => WindhawkMods.DisplayName(m.ModId).Split(' ')[0]).ToList();
            parts.AddRange(Package.Apps.OrderBy(a => a.Kind)
                .Select(a => a.Kind == AppThemeKind.WindowsTerminal ? "Terminal" : ThemeAppConfig.DisplayName(a.Kind)));
            if (Package.Wallpapers.Count > 0)
                parts.Add($"{Package.Wallpapers.Count} wallpapers");
            return string.Join(" · ", parts);
        }
    }

    public string ImportedText => Package.ImportedAt.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
}

public sealed partial class ThemeModRow(ThemeModConfig config, ModState state) : ObservableObject
{
    public ThemeModConfig Config { get; } = config;
    public string Name => WindhawkMods.DisplayName(Config.ModId);
    public bool IsInstalled => State != ModState.NotInstalled;
    public ModState State { get; } = state;

    public string StateText => State switch
    {
        ModState.Enabled => $"ready · {Config.SettingCount} settings",
        ModState.Disabled => "mod is disabled in Windhawk",
        _ => "mod not installed",
    };

    [ObservableProperty] private bool _isSelected = state != ModState.NotInstalled;
}

/// <summary>앱 테마 한 줄 (Windows Terminal / Discord / Komorebi / YASB).</summary>
public sealed partial class ThemeAppRow : ObservableObject
{
    private readonly string _availableSuffix = "";
    private readonly string _unavailableText = "";

    public ThemeAppRow(ThemeAppConfig config, AppThemeTargets targets)
    {
        Config = config;
        Variants = config.AllVariants();
        _selectedVariant = Variants[0];
        switch (config.Kind)
        {
            case AppThemeKind.WindowsTerminal:
                IsAvailable = WindowsTerminalThemer.FindSettingsFiles(targets.LocalAppData).Count > 0;
                _unavailableText = "Windows Terminal not found";
                GetLinkText = "Get Terminal ↗";
                GetLink = "ms-windows-store://pdp/?productid=9N0DX20HK701";
                ToolTip = "Adds the theme's color schemes and appearance (font, opacity, padding) to your Terminal settings.\n"
                          + "Your profiles, key bindings and other settings are kept.";
                break;
            case AppThemeKind.Discord:
                var clients = DiscordThemer.FindClients(targets.AppData);
                IsAvailable = clients.Count > 0;
                _availableSuffix = " · " + string.Join(", ", clients.Select(c => c.Name));
                _unavailableText = "needs Vencord or BetterDiscord";
                GetLinkText = "Get Vencord ↗";
                GetLink = "https://vencord.dev/download/";
                ToolTip = "Plain Discord cannot load themes. MoniMS puts the theme into a client mod you already use\n"
                          + "(Vencord, Vesktop, Equicord or BetterDiscord) and turns it on.\n"
                          + "A Discord theme applied earlier from your library is turned off, so the two don't mix.\n"
                          + "Client mods are not allowed by Discord's Terms of Service. Use them at your own risk.";
                break;
            case AppThemeKind.Komorebi:
                IsAvailable = KomorebiThemer.IsInstalled(targets.UserProfile);
                _unavailableText = "Komorebi not found";
                GetLinkText = "Get Komorebi ↗";
                GetLink = "https://github.com/LGUG2Z/komorebi/releases/latest";
                ToolTip = "Adds the theme's look to your komorebi.json: color theme, borders, padding, transparency and animation.\n"
                          + "Your monitors, workspaces and rules are kept. If you have no komorebi.json yet, the theme's file is used.\n"
                          + "whkd key bindings (whkdrc) are added only if you don't have your own.";
                break;
            case AppThemeKind.Yasb:
                IsAvailable = YasbThemer.IsInstalled(targets.UserProfile);
                _unavailableText = "YASB not found";
                GetLinkText = "Get YASB ↗";
                GetLink = "https://github.com/amnweb/yasb/releases/latest";
                ToolTip = "Replaces your YASB config.yaml (bar layout and widgets) and styles.css with the theme's,\n"
                          + "because a YASB theme's styles only match its own layout. Press Restore to get yours back.\n"
                          + "YASB reloads by itself in a moment.";
                break;
            default:
                IsAvailable = false;
                GetLinkText = "";
                GetLink = "";
                ToolTip = "";
                break;
        }
        _isSelected = IsAvailable;
    }

    public ThemeAppConfig Config { get; }
    public string Name => ThemeAppConfig.DisplayName(Config.Kind);
    public bool IsAvailable { get; }
    public string GetLinkText { get; }
    public string GetLink { get; }
    public string ToolTip { get; }

    /// <summary>패키지에 이 앱용 테마가 여러 개 있으면 고를 목록을 보여준다.</summary>
    public IReadOnlyList<ThemeAppVariant> Variants { get; }
    public bool HasVariants => Variants.Count > 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private ThemeAppVariant _selectedVariant;

    public string StateText => IsAvailable ? SelectedVariant.Summary + _availableSuffix : _unavailableText;

    [ObservableProperty] private bool _isSelected;
}

public sealed class WallpaperThumb(string relativePath, ImageSource? thumbnail)
{
    public string RelativePath { get; } = relativePath;
    public string Name => Path.GetFileNameWithoutExtension(RelativePath);
    public ImageSource? Thumbnail { get; } = thumbnail;
}

/// <summary>설정 창 "Themes" 탭: 테마 가져오기 / 보관함 / 적용(Windhawk·Terminal·Discord) / 되돌리기.</summary>
public sealed partial class ShellThemesViewModel : ObservableObject, IDisposable
{
    private readonly ThemeLibrary _library;
    private readonly ThemeImporter _importer;
    private readonly ThemeApplier _applier;
    private readonly IWindhawkStorage _storage;
    private readonly ISettingsStore _settings;
    private readonly ILogger<ShellThemesViewModel> _logger;
    private CancellationTokenSource? _thumbsCts;

    public ShellThemesViewModel(ThemeLibrary library, ThemeImporter importer, ThemeApplier applier,
        IWindhawkStorage storage, ISettingsStore settings, ILogger<ShellThemesViewModel> logger)
    {
        _library = library;
        _importer = importer;
        _applier = applier;
        _storage = storage;
        _settings = settings;
        _logger = logger;
        Refresh();
    }

    // ---------- Windhawk 상태 ----------
    [ObservableProperty] private bool _isWindhawkInstalled;
    [ObservableProperty] private string _windhawkStatus = "";

    // ---------- 가져오기 ----------
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _importText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand), nameof(ApplyCommand), nameof(RestoreCommand), nameof(ResetToOriginalCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _status = "";

    // ---------- 보관함 / 선택 ----------
    public ObservableCollection<ThemeListItem> Themes { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(DeleteThemeCommand), nameof(OpenThemeFolderCommand))]
    private ThemeListItem? _selectedTheme;

    public bool HasSelection => SelectedTheme is not null;

    public ObservableCollection<ThemeModRow> ModRows { get; } = [];
    public ObservableCollection<ThemeAppRow> AppRows { get; } = [];
    public ObservableCollection<WallpaperThumb> Wallpapers { get; } = [];

    [ObservableProperty] private WallpaperThumb? _selectedWallpaper;
    [ObservableProperty] private bool _applyWallpaper;
    [ObservableProperty] private bool _hasCustomScript;
    [ObservableProperty] private bool _allowCustomScript;
    /// <summary>null이면 표시 안 함.</summary>
    [ObservableProperty] private string? _otherItemsText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private string? _backupLabel;

    /// <summary>테마 적용 전 원래 상태로 되돌릴 것이 있는지.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetToOriginalCommand))]
    private bool _hasOriginal;

    private void UpdateBackupState()
    {
        BackupLabel = _applier.LatestBackupLabel;
        HasOriginal = _applier.HasOriginal;
    }

    partial void OnSelectedThemeChanged(ThemeListItem? value) => LoadDetails(value?.Package);

    partial void OnSelectedWallpaperChanged(WallpaperThumb? value)
    {
        if (value is not null)
            ApplyWallpaper = true;
    }

    [RelayCommand]
    private void Refresh()
    {
        var selectedId = SelectedTheme?.Package.Id;
        _storage.CustomAppRoot = _settings.Current.WindhawkPath;
        var info = _storage.Detect();
        IsWindhawkInstalled = info.Installed;
        WindhawkStatus = info.Installed
            ? $"Windhawk {info.Version ?? ""} found{(info.Portable ? " (portable)" : "")}".Replace("  ", " ")
            : "Windhawk not found — it is required to restyle the taskbar and Start menu.";

        Themes.Clear();
        foreach (var p in _library.GetAll())
            Themes.Add(new ThemeListItem(p));
        SelectedTheme = Themes.FirstOrDefault(t => t.Package.Id == selectedId) ?? Themes.FirstOrDefault();
        UpdateBackupState();
    }

    private string? _detailsPackageId;

    private void LoadDetails(ThemePackage? package)
    {
        // 같은 테마를 다시 불러올 때(적용 후 상태 갱신)는 고른 앱 테마를 유지
        var keep = package is not null && package.Id == _detailsPackageId
            ? AppRows.ToDictionary(r => r.Config.Kind, r => r.SelectedVariant.SourceFile)
            : [];
        _detailsPackageId = package?.Id;
        ModRows.Clear();
        AppRows.Clear();
        Wallpapers.Clear();
        SelectedWallpaper = null;
        ApplyWallpaper = false;
        AllowCustomScript = false;
        HasCustomScript = false;
        OtherItemsText = null;
        _thumbsCts?.Cancel();
        _thumbsCts?.Dispose();
        _thumbsCts = null;
        if (package is null)
            return;

        foreach (var mod in package.Mods.OrderBy(m => WindhawkMods.All.ToList().IndexOf(m.ModId)))
            ModRows.Add(new ThemeModRow(mod, _storage.GetModState(mod.ModId)));
        foreach (var app in package.Apps.OrderBy(a => a.Kind))
        {
            var row = new ThemeAppRow(app, _applier.Targets);
            if (keep.TryGetValue(app.Kind, out var file) && row.Variants.FirstOrDefault(v => v.SourceFile == file) is { } variant)
                row.SelectedVariant = variant;
            AppRows.Add(row);
        }
        HasCustomScript = package.Mods.Any(m => m.HasCustomScript);
        OtherItemsText = package.OtherItems.Count == 0 ? null : string.Join(", ", package.OtherItems);

        // 썸네일은 큰 이미지라 백그라운드에서 디코딩
        var cts = _thumbsCts = new CancellationTokenSource();
        var items = package.Wallpapers.ToList();
        var dispatcher = Application.Current.Dispatcher;
        _ = Task.Run(() =>
        {
            foreach (var rel in items)
            {
                if (cts.IsCancellationRequested)
                    return;
                var thumb = LoadThumbnail(package.FullPath(rel));
                dispatcher.BeginInvoke(() =>
                {
                    if (!cts.IsCancellationRequested)
                        Wallpapers.Add(new WallpaperThumb(rel, thumb));
                });
            }
        }, cts.Token);
    }

    private static BitmapImage? LoadThumbnail(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 220;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---------- 가져오기 ----------

    private bool CanImport() => !IsBusy && !string.IsNullOrWhiteSpace(ImportText);

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        IsBusy = true;
        try
        {
            var progress = new Progress<string>(s => Status = s);
            // 로컬 파일은 압축 풀기·분석이 전부 동기라서 UI 스레드에서 돌면 창이 멈추고,
            // 진행 메시지("Analyzing...")가 완료 메시지 뒤에 도착해 그대로 남는다
            var input = ImportText;
            var package = await Task.Run(() => _importer.ImportAsync(input, progress));
            ImportText = "";
            Refresh();
            SelectedTheme = Themes.FirstOrDefault(t => t.Package.Id == package.Id);
            Status = $"Imported '{package.Name}': {package.Mods.Count + package.Apps.Count} part(s), {package.Wallpapers.Count} wallpaper(s).";
        }
        catch (InvalidThemeFormatException ex)
        {
            _logger.LogInformation("Rejected theme import: {Reason}", ex.Message);
            Status = "Invalid file format.";
            MessageBox.Show("Invalid file format.\n\n" + ex.Message + "\n\nHover the ⓘ next to \"Import a theme\" to see what can be imported.",
                "MoniMS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Theme import failed");
            Status = "Import failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void BrowseFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a theme package",
            Filter = "Theme package (*.zip;*.json;*.css)|*.zip;*.json;*.css|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() == true)
        {
            ImportText = dialog.FileName;
            ImportCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a theme folder" };
        if (dialog.ShowDialog() == true)
        {
            ImportText = dialog.FolderName;
            ImportCommand.Execute(null);
        }
    }

    [RelayCommand]
    private static void BrowseOnline() =>
        OpenUrl("https://github.com/search?q=windhawk+taskbar+theme&type=repositories");

    // ---------- 적용 / 되돌리기 ----------

    private bool CanApply() => !IsBusy && SelectedTheme is not null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var package = SelectedTheme!.Package;
        var modIds = ModRows.Where(r => r.IsSelected && r.IsInstalled).Select(r => r.Config.ModId).ToList();
        var chosenApps = AppRows.Where(r => r.IsSelected && r.IsAvailable).ToList();
        var apps = chosenApps.Select(r => r.Config.Kind).ToList();
        var variants = chosenApps.ToDictionary(r => r.Config.Kind, r => r.SelectedVariant.SourceFile);
        var wallpaper = ApplyWallpaper ? SelectedWallpaper?.RelativePath ?? package.Wallpapers.FirstOrDefault() : null;
        if (modIds.Count == 0 && apps.Count == 0 && wallpaper is null)
        {
            Status = "Nothing selected to apply.";
            return;
        }

        IsBusy = true;
        Status = "Applying...";
        try
        {
            var options = new ThemeApplyOptions(modIds, AllowCustomScript, wallpaper, apps, variants);
            // 관리자 권한 헬퍼를 기다릴 수 있으므로 UI 스레드 밖에서
            var result = modIds.Count > 0 || apps.Count > 0
                ? await Task.Run(() => _applier.Apply(package, options))
                : new ThemeApplyResult([], false, []);

            var wallpaperSet = wallpaper is not null && _applier.ApplyWallpaper(package, wallpaper);

            var parts = result.AppliedMods.Select(WindhawkMods.DisplayName).Concat(result.Apps).ToList();
            if (wallpaperSet)
                parts.Add("wallpaper");
            Status = (parts.Count > 0 ? $"Applied: {string.Join(", ", parts)}. Changes appear in a few seconds." : "Nothing was applied.")
                     + (result.Warnings.Count > 0 ? "\n" + string.Join("\n", result.Warnings) : "");
        }
        catch (OperationCanceledException ex)
        {
            Status = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Theme apply failed");
            Status = "Apply failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            UpdateBackupState();
            LoadDetails(package); // 모드 상태 갱신
        }
    }

    private bool CanResetToOriginal() => !IsBusy && HasOriginal;

    /// <summary>라이브러리 테마를 적용하기 전(처음) 상태로 한 번에 되돌린다.</summary>
    [RelayCommand(CanExecute = nameof(CanResetToOriginal))]
    private async Task ResetToOriginalAsync()
    {
        if (MessageBox.Show(
                "Go back to how Windows looked before you applied any theme from the library?\n\n"
                + "This undoes every theme apply at once: taskbar & Start menu (Windhawk), Windows Terminal, Discord, Komorebi, YASB and wallpaper.",
                "MoniMS", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        IsBusy = true;
        Status = "Resetting...";
        try
        {
            var restored = (await Task.Run(_applier.ResetToOriginal)).ToList();
            if (_applier.RestoreOriginalWallpaper())
                restored.Add("wallpaper");
            Status = restored.Count > 0
                ? $"Back to your original look: {string.Join(", ", restored)}."
                : "Nothing needed resetting.";
        }
        catch (OperationCanceledException ex)
        {
            Status = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Theme reset failed");
            Status = "Reset failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            UpdateBackupState();
            LoadDetails(SelectedTheme?.Package);
        }
    }

    private bool CanRestore() => !IsBusy && BackupLabel is not null;

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync()
    {
        if (MessageBox.Show($"Restore the settings saved {BackupLabel}?\n(Windhawk mods, Windows Terminal, Discord, Komorebi and YASB files)", "MoniMS",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        IsBusy = true;
        try
        {
            var restored = await Task.Run(_applier.RestoreLatest);
            Status = restored.Count > 0 ? $"Restored: {string.Join(", ", restored)}." : "Nothing needed restoring.";
        }
        catch (OperationCanceledException ex)
        {
            Status = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Theme restore failed");
            Status = "Restore failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            UpdateBackupState();
        }
    }

    // ---------- 보관함 관리 ----------

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteTheme()
    {
        var p = SelectedTheme!.Package;
        if (MessageBox.Show($"Remove '{p.Name}' from the library?\nSettings already applied stay as they are.", "MoniMS",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            _thumbsCts?.Cancel();
            _library.Delete(p);
            SelectedTheme = null;
            Refresh();
        }
        catch (Exception ex)
        {
            Status = "Delete failed: " + ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenThemeFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SelectedTheme!.Package.FilesDirectory}\"") { UseShellExecute = true });

    // ---------- Windhawk ----------

    [RelayCommand]
    private static void GetWindhawk() => OpenUrl("https://windhawk.net/");

    [RelayCommand]
    private void InstallMod(ThemeModRow? row)
    {
        if (row is null)
            return;
        var name = WindhawkMods.StoreName(row.Config.ModId);
        // windhawk:// 링크는 Windhawk 2.0부터. 1.x에서는 편집기 창만 열리므로 웹 페이지로 안내.
        var version = _storage.Detect().Version;
        if (WindhawkMods.SupportsDeepLinks(version) && OpenUrl(WindhawkMods.InstallLink(row.Config.ModId)))
        {
            Status = $"Install '{name}' in Windhawk, then press ↻ to refresh.";
            return;
        }
        OpenUrl(WindhawkMods.WebLink(row.Config.ModId));
        Status = $"In Windhawk, open Explore, search '{name}' and press Install. Then press ↻ to refresh.";
    }

    [RelayCommand]
    private void GetApp(ThemeAppRow? row)
    {
        if (row is null || row.GetLink.Length == 0)
            return;
        if (!OpenUrl(row.GetLink) && row.Config.Kind == AppThemeKind.WindowsTerminal)
            OpenUrl("https://aka.ms/terminal");
        Status = $"Install it, then press ↻ to refresh.";
    }

    [RelayCommand]
    private void LocateWindhawk()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the Windhawk folder (contains windhawk.ini)" };
        if (dialog.ShowDialog() != true)
            return;
        if (!File.Exists(Path.Combine(dialog.FolderName, "windhawk.ini")))
        {
            Status = "That folder has no windhawk.ini.";
            return;
        }
        _settings.Current.WindhawkPath = dialog.FolderName;
        _settings.Save();
        Refresh();
    }

    public void Dispose()
    {
        _thumbsCts?.Cancel();
        _thumbsCts?.Dispose();
        _thumbsCts = null;
    }

    private static bool OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
