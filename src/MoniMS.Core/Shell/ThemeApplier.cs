using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MoniMS.Core.Desktop;
using MoniMS.Core.Presets;
using MoniMS.Core.Shell.Apps;

namespace MoniMS.Core.Shell;

public sealed record ModSettingsWrite(string ModId, IReadOnlyDictionary<string, object> Settings);

/// <summary>Windhawk 설정 쓰기. 앱에서는 권한이 부족하면 관리자 권한으로 다시 시도하는 구현을 쓴다.</summary>
public interface IWindhawkSettingsWriter
{
    void Write(IReadOnlyList<ModSettingsWrite> items);
}

/// <param name="AppVariants">앱별로 고른 테마 파일 (files\ 기준 상대 경로). 없으면 그 앱의 기본 테마.</param>
public sealed record ThemeApplyOptions(
    IReadOnlyCollection<string> ModIds,
    bool AllowCustomScript,
    string? WallpaperRelativePath,
    IReadOnlyCollection<AppThemeKind>? Apps = null,
    IReadOnlyDictionary<AppThemeKind, string>? AppVariants = null);

public sealed record ThemeApplyResult(
    IReadOnlyList<string> AppliedMods,
    bool WallpaperSet,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string>? AppliedApps = null)
{
    public IReadOnlyList<string> Apps => AppliedApps ?? [];
}

/// <summary>앱 테마를 적용할 위치. 테스트에서는 임시 폴더를 넘긴다.</summary>
public sealed record AppThemeTargets(
    string? LocalAppData = null,
    string? AppData = null,
    Func<string, bool>? FontInstalled = null,
    string? UserProfile = null)
{
    public static readonly AppThemeTargets System = new();

    /// <summary>실제 위치에 적용하는지 (그때만 실행 중인 앱에 다시 불러오라고 알린다).</summary>
    public bool IsSystem => LocalAppData is null && AppData is null && UserProfile is null;
}

/// <summary>테마 적용 / 이전 설정 백업·복원.</summary>
public sealed class ThemeApplier
{
    private readonly IWindhawkStorage _storage;
    private readonly IWindhawkSettingsWriter _writer;
    private readonly IWallpaperService _wallpaper;
    private readonly ThemeLibrary _library;
    private readonly AppThemeTargets _targets;
    private readonly ILogger<ThemeApplier> _logger;

    public ThemeApplier(IWindhawkStorage storage, IWindhawkSettingsWriter writer, IWallpaperService wallpaper,
        ThemeLibrary library, ILogger<ThemeApplier>? logger = null, AppThemeTargets? targets = null)
    {
        _storage = storage;
        _writer = writer;
        _wallpaper = wallpaper;
        _library = library;
        _targets = targets ?? AppThemeTargets.System;
        _logger = logger ?? NullLogger<ThemeApplier>.Instance;
    }

    public AppThemeTargets Targets => _targets;

    /// <summary>
    /// Windhawk 설정 + 앱 테마(Terminal, Discord, Komorebi, YASB) 적용 (백그라운드 스레드에서 호출 가능).
    /// 바꾸기 전 상태는 한 개의 백업 파일에 모아 두어 Restore 한 번으로 되돌린다.
    /// </summary>
    public ThemeApplyResult Apply(ThemePackage package, ThemeApplyOptions options)
    {
        var warnings = new List<string>();
        var writes = PrepareModWrites(package, options, warnings);

        // 1) Windhawk: 관리자 권한 창에서 취소하면 여기서 예외 → 아무것도 바뀌지 않음
        Dictionary<string, Dictionary<string, JsonElement>>? modBackup = null;
        if (writes.Count > 0)
        {
            modBackup = SnapshotMods(writes.Select(w => w.ModId));
            _writer.Write(writes);
        }

        // 2) 앱 테마: 바꾸는 파일의 원래 내용을 journal에 기록
        var journal = new FileJournal();
        var appliedApps = new List<string>();
        try
        {
            foreach (var app in package.Apps.Where(a => options.Apps?.Contains(a.Kind) == true))
                ApplyApp(package, app, options.AppVariants?.GetValueOrDefault(app.Kind), journal, warnings, appliedApps);
        }
        finally
        {
            if (modBackup is not null || journal.Originals.Count > 0)
                SaveBackup(package.Name, modBackup ?? [], journal);
        }

        foreach (var w in writes.Where(w => _storage.GetModState(w.ModId) == ModState.Disabled))
            warnings.Add($"{WindhawkMods.DisplayName(w.ModId)}: the mod is disabled in Windhawk. Enable it to see the change.");

        return new ThemeApplyResult(writes.Select(w => w.ModId).ToList(), false, warnings, appliedApps);
    }

    private List<ModSettingsWrite> PrepareModWrites(ThemePackage package, ThemeApplyOptions options, List<string> warnings)
    {
        var writes = new List<ModSettingsWrite>();
        var selected = package.Mods.Where(m => options.ModIds.Contains(m.ModId)).ToList();
        if (selected.Count == 0)
            return writes;
        if (!_storage.Detect().Installed)
            throw new InvalidOperationException("Windhawk is not installed. Install it from windhawk.net first.");

        foreach (var mod in selected)
        {
            if (_storage.GetModState(mod.ModId) == ModState.NotInstalled)
            {
                warnings.Add($"{WindhawkMods.DisplayName(mod.ModId)}: the Windhawk mod is not installed, skipped.");
                continue;
            }
            var settings = ThemeScanner.TryReadFlatSettings(package.FullPath(mod.SourceFile));
            if (settings is null)
            {
                warnings.Add($"{WindhawkMods.DisplayName(mod.ModId)}: settings file could not be read.");
                continue;
            }
            if (mod.HasCustomScript && !options.AllowCustomScript)
            {
                settings = ThemeScanner.WithoutCustomScript(settings);
                warnings.Add($"{WindhawkMods.DisplayName(mod.ModId)}: custom script was not applied.");
            }
            writes.Add(new ModSettingsWrite(mod.ModId, settings));
        }
        return writes;
    }

    private void ApplyApp(ThemePackage package, ThemeAppConfig app, string? variant, FileJournal journal, List<string> warnings, List<string> applied)
    {
        var name = ThemeAppConfig.DisplayName(app.Kind);
        try
        {
            // 고른 테마가 이 패키지의 것인지 확인 (아니면 기본 테마)
            var chosen = app.AllVariants().FirstOrDefault(v => v.SourceFile.Equals(variant, StringComparison.OrdinalIgnoreCase))
                         ?? app.AllVariants()[0];
            var source = package.FullPath(chosen.SourceFile);
            switch (app.Kind)
            {
                case AppThemeKind.WindowsTerminal:
                    if (WindowsTerminalThemer.Apply(source, journal, warnings, _targets.FontInstalled, _targets.LocalAppData) > 0)
                        applied.Add(name);
                    break;
                case AppThemeKind.Discord:
                    var clients = DiscordThemer.Apply(source, journal, warnings, _targets.AppData, _targets.FontInstalled,
                        LibraryDiscordThemes(package));
                    if (clients.Count > 0)
                        applied.Add($"{name} ({string.Join(", ", clients)})");
                    break;
                case AppThemeKind.Komorebi:
                    if (!KomorebiThemer.IsInstalled(_targets.UserProfile))
                        warnings.Add("Komorebi is not installed, skipped.");
                    else if (KomorebiThemer.Apply(source, journal, warnings, _targets.UserProfile, reload: _targets.IsSystem) > 0)
                        applied.Add(name);
                    break;
                case AppThemeKind.Yasb:
                    if (!YasbThemer.IsInstalled(_targets.UserProfile))
                        warnings.Add("YASB is not installed, skipped.");
                    else if (YasbThemer.Apply(source, journal, warnings, _targets.UserProfile, _targets.FontInstalled) > 0)
                        applied.Add(Path.GetFileName(Path.GetDirectoryName(source)) is { } folder && app.Variants.Count > 0 ? $"{name} ({folder})" : name);
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            // 한 앱이 실패해도 나머지는 계속. 이미 바뀐 파일은 journal에 있어 복원 가능.
            _logger.LogWarning(ex, "Applying {App} theme failed", name);
            warnings.Add($"{name}: could not apply ({ex.Message}).");
        }
    }

    /// <summary>
    /// 보관함에 있는 모든 Discord 테마. 새 테마를 켤 때 예전에 MoniMS로 켠 테마를 꺼서 두 테마가 겹치지 않게 한다.
    /// </summary>
    private List<(string FileName, string ThemeName)> LibraryDiscordThemes(ThemePackage current)
    {
        var result = new List<(string, string)>();
        foreach (var p in _library.GetAll().Where(p => p.Id != current.Id).Append(current))
        {
            foreach (var variant in p.Apps.Where(a => a.Kind == AppThemeKind.Discord).SelectMany(a => a.AllVariants()))
            {
                var file = p.FullPath(variant.SourceFile);
                if (File.Exists(file))
                    result.Add(DiscordThemer.Identify(file));
            }
        }
        return result;
    }

    /// <summary>배경화면 적용 (COM이라 UI 스레드에서 호출).</summary>
    public bool ApplyWallpaper(ThemePackage package, string relativePath)
    {
        var full = Path.GetFullPath(package.FullPath(relativePath));
        if (!full.StartsWith(Path.GetFullPath(package.FilesDirectory), StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            return false;
        SaveOriginalWallpaper();
        _wallpaper.SetForAllMonitors(full, WallpaperPosition.Fill);
        return true;
    }

    private string OriginalWallpaperDirectory => Path.Combine(_library.BackupDirectory, "original-wallpaper");
    private string OriginalWallpaperFile => Path.Combine(OriginalWallpaperDirectory, "wallpaper.json");

    /// <summary>테마 배경화면을 처음 적용할 때만 지금 배경화면을 저장 (Reset to original 용).</summary>
    private void SaveOriginalWallpaper()
    {
        if (File.Exists(OriginalWallpaperFile))
            return;
        try
        {
            Directory.CreateDirectory(OriginalWallpaperDirectory);
            var settings = _wallpaper.Capture(OriginalWallpaperDirectory);
            File.WriteAllText(OriginalWallpaperFile, JsonSerializer.Serialize(settings, JsonDefaults.Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogWarning(ex, "Could not save the original wallpaper");
        }
    }

    // ---------- 백업 / 복원 ----------

    private sealed class BackupFile
    {
        public string ThemeName { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
        public Dictionary<string, Dictionary<string, JsonElement>> Mods { get; set; } = [];

        /// <summary>앱 테마로 바뀐 파일 → 원래 내용 (null이면 원래 없던 파일).</summary>
        public Dictionary<string, string?> Files { get; set; } = [];
    }

    private Dictionary<string, Dictionary<string, JsonElement>> SnapshotMods(IEnumerable<string> modIds) =>
        modIds.Distinct().ToDictionary(id => id,
            id => _storage.ReadSettings(id).ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value)));

    private void SaveBackup(string themeName, Dictionary<string, Dictionary<string, JsonElement>> mods, FileJournal journal)
    {
        var backup = new BackupFile
        {
            ThemeName = themeName,
            Mods = mods,
            Files = journal.Originals.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
        };
        Directory.CreateDirectory(_library.BackupDirectory);
        string path;
        while (File.Exists(path = Path.Combine(_library.BackupDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.json")))
            Thread.Sleep(2); // 같은 밀리초에 두 번 적용한 경우 (이름 순서 = 시간 순서 유지)
        File.WriteAllText(path, JsonSerializer.Serialize(backup, JsonDefaults.Options));
        _logger.LogInformation("Backed up {Mods} mod(s) and {Files} file(s) to {Path}", mods.Count, backup.Files.Count, path);
    }

    public bool HasBackup => LatestBackup() is not null;

    /// <summary>가장 최근 백업의 설명 (UI 표시용).</summary>
    public string? LatestBackupLabel
    {
        get
        {
            var file = LatestBackup();
            if (file is null)
                return null;
            try
            {
                var b = JsonSerializer.Deserialize<BackupFile>(File.ReadAllText(file), JsonDefaults.Options);
                return b is null ? null : $"before '{b.ThemeName}' ({b.CreatedAt:MM-dd HH:mm})";
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 가장 최근 백업으로 되돌리고 그 백업을 지운다 (여러 번 누르면 더 이전으로).
    /// 되돌린 항목 이름 목록 반환 (표시용: "Taskbar Styler", "Windows Terminal" ...).
    /// </summary>
    public IReadOnlyList<string> RestoreLatest()
    {
        var file = LatestBackup() ?? throw new InvalidOperationException("There is nothing to restore.");
        var backup = ReadBackup(file) ?? throw new InvalidDataException("Backup file is damaged.");
        var restored = RestoreState(backup.Mods, backup.Files);
        File.Delete(file);
        return restored;
    }

    /// <summary>Reset to original 로 되돌릴 것이 있는지 (테마 백업 또는 저장된 원래 배경화면).</summary>
    public bool HasOriginal => HasBackup || File.Exists(OriginalWallpaperFile);

    /// <summary>
    /// 테마를 처음 적용하기 전 상태로 한 번에 되돌린다 (백그라운드 스레드에서 호출 가능).
    /// 항목마다 "가장 오래된 백업"의 값이 테마 적용 전 원래 값이다. 성공하면 모든 백업을 지운다.
    /// 배경화면은 COM이라 <see cref="RestoreOriginalWallpaper"/>를 UI 스레드에서 따로 호출.
    /// </summary>
    public IReadOnlyList<string> ResetToOriginal()
    {
        var files = Directory.Exists(_library.BackupDirectory)
            ? Directory.GetFiles(_library.BackupDirectory, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToList()
            : [];
        var mods = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.Ordinal);
        var originals = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var backup = ReadBackup(file);
            if (backup is null)
                continue;
            foreach (var (id, settings) in backup.Mods)
                mods.TryAdd(id, settings);
            foreach (var (path, text) in backup.Files)
                originals.TryAdd(path, text);
        }

        var restored = RestoreState(mods, originals); // 관리자 권한 창을 취소하면 예외 → 백업 유지
        foreach (var file in files)
            File.Delete(file);
        return restored;
    }

    /// <summary>저장해 둔 원래 배경화면으로 되돌린다 (UI 스레드). 저장된 것이 없으면 false.</summary>
    public bool RestoreOriginalWallpaper()
    {
        if (!File.Exists(OriginalWallpaperFile))
            return false;
        var settings = JsonSerializer.Deserialize<WallpaperSettings>(File.ReadAllText(OriginalWallpaperFile), JsonDefaults.Options);
        if (settings is null)
            return false;
        _wallpaper.Apply(settings, OriginalWallpaperDirectory);
        try
        {
            Directory.Delete(OriginalWallpaperDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 배경화면 파일이 아직 쓰이는 중일 수 있음 → wallpaper.json만이라도 지워 "되돌림 완료"로 둔다
            File.Delete(OriginalWallpaperFile);
        }
        return true;
    }

    private BackupFile? ReadBackup(string file)
    {
        try
        {
            return JsonSerializer.Deserialize<BackupFile>(File.ReadAllText(file), JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Damaged backup {File}", file);
            return null;
        }
    }

    /// <summary>모드 설정과 파일을 주어진 상태로 되돌리고, 되돌린 항목 이름을 반환.</summary>
    private List<string> RestoreState(Dictionary<string, Dictionary<string, JsonElement>> mods, Dictionary<string, string?> files)
    {
        var writes = mods
            .Where(kv => _storage.GetModState(kv.Key) != ModState.NotInstalled)
            .Select(kv => new ModSettingsWrite(kv.Key, ToSettings(kv.Value)))
            .ToList();
        if (writes.Count > 0)
            _writer.Write(writes); // 관리자 권한 창을 취소하면 여기서 예외

        var restored = writes.Select(w => WindhawkMods.DisplayName(w.ModId)).ToList();
        foreach (var (path, original) in files)
        {
            try
            {
                if (original is null)
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, original, new System.Text.UTF8Encoding(false));
                }
                var label = ThemeAppConfig.DisplayNameForPath(path);
                if (!restored.Contains(label))
                    restored.Add(label);
                if (_targets.IsSystem && original is not null &&
                    Path.GetFileName(path).Equals(KomorebiThemer.ConfigFileName, StringComparison.OrdinalIgnoreCase))
                    KomorebiThemer.Reload(path); // 실행 중인 komorebi에 되돌린 설정을 불러오게
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not restore {Path}", path);
            }
        }
        return restored;
    }

    private string? LatestBackup() =>
        Directory.Exists(_library.BackupDirectory)
            ? Directory.GetFiles(_library.BackupDirectory, "*.json").OrderByDescending(f => f, StringComparer.Ordinal).FirstOrDefault()
            : null;

    internal static Dictionary<string, object> ToSettings(Dictionary<string, JsonElement> raw) =>
        raw.ToDictionary(kv => kv.Key, kv => kv.Value.ValueKind == JsonValueKind.Number && kv.Value.TryGetInt32(out var i)
            ? (object)i
            : kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() ?? "" : kv.Value.ToString(), StringComparer.Ordinal);

    /// <summary>관리자 권한 헬퍼와 주고받는 요청 파일 형식.</summary>
    public static string SerializeWrites(IReadOnlyList<ModSettingsWrite> items) =>
        JsonSerializer.Serialize(items.ToDictionary(i => i.ModId, i => i.Settings), JsonDefaults.Options);

    public static IReadOnlyList<ModSettingsWrite> DeserializeWrites(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(json, JsonDefaults.Options) ?? [];
        // 헬퍼는 관리자 권한으로 돌므로 모드 ID를 엄격히 확인 (임의 레지스트리 경로 방지)
        foreach (var id in raw.Keys)
        {
            if (!WindhawkMods.All.Contains(id))
                throw new InvalidDataException($"Unexpected mod id: {id}");
        }
        return raw.Select(kv => new ModSettingsWrite(kv.Key, ToSettings(kv.Value))).ToList();
    }
}
