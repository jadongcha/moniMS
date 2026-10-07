using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MoniMS.Core.Desktop;
using MoniMS.Core.Presets;

namespace MoniMS.Core.Shell;

public sealed record ModSettingsWrite(string ModId, IReadOnlyDictionary<string, object> Settings);

/// <summary>Windhawk 설정 쓰기. 앱에서는 권한이 부족하면 관리자 권한으로 다시 시도하는 구현을 쓴다.</summary>
public interface IWindhawkSettingsWriter
{
    void Write(IReadOnlyList<ModSettingsWrite> items);
}

public sealed record ThemeApplyOptions(
    IReadOnlyCollection<string> ModIds,
    bool AllowCustomScript,
    string? WallpaperRelativePath);

public sealed record ThemeApplyResult(IReadOnlyList<string> AppliedMods, bool WallpaperSet, IReadOnlyList<string> Warnings);

/// <summary>테마 적용 / 이전 설정 백업·복원.</summary>
public sealed class ThemeApplier
{
    private readonly IWindhawkStorage _storage;
    private readonly IWindhawkSettingsWriter _writer;
    private readonly IWallpaperService _wallpaper;
    private readonly ThemeLibrary _library;
    private readonly ILogger<ThemeApplier> _logger;

    public ThemeApplier(IWindhawkStorage storage, IWindhawkSettingsWriter writer, IWallpaperService wallpaper,
        ThemeLibrary library, ILogger<ThemeApplier>? logger = null)
    {
        _storage = storage;
        _writer = writer;
        _wallpaper = wallpaper;
        _library = library;
        _logger = logger ?? NullLogger<ThemeApplier>.Instance;
    }

    /// <summary>Windhawk 설정 부분만 적용 (백그라운드 스레드에서 호출 가능).</summary>
    public ThemeApplyResult ApplyMods(ThemePackage package, ThemeApplyOptions options)
    {
        var warnings = new List<string>();
        var writes = new List<ModSettingsWrite>();

        var info = _storage.Detect();
        var selected = package.Mods.Where(m => options.ModIds.Contains(m.ModId)).ToList();
        if (selected.Count > 0 && !info.Installed)
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

        if (writes.Count > 0)
        {
            Backup(package.Name, writes.Select(w => w.ModId));
            _writer.Write(writes);
        }

        foreach (var w in writes.Where(w => _storage.GetModState(w.ModId) == ModState.Disabled))
            warnings.Add($"{WindhawkMods.DisplayName(w.ModId)}: the mod is disabled in Windhawk. Enable it to see the change.");

        return new ThemeApplyResult(writes.Select(w => w.ModId).ToList(), false, warnings);
    }

    /// <summary>배경화면 적용 (COM이라 UI 스레드에서 호출).</summary>
    public bool ApplyWallpaper(ThemePackage package, string relativePath)
    {
        var full = Path.GetFullPath(package.FullPath(relativePath));
        if (!full.StartsWith(Path.GetFullPath(package.FilesDirectory), StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            return false;
        _wallpaper.SetForAllMonitors(full, WallpaperPosition.Fill);
        return true;
    }

    // ---------- 백업 / 복원 ----------

    private sealed class BackupFile
    {
        public string ThemeName { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
        public Dictionary<string, Dictionary<string, JsonElement>> Mods { get; set; } = [];
    }

    private void Backup(string themeName, IEnumerable<string> modIds)
    {
        var backup = new BackupFile { ThemeName = themeName };
        foreach (var id in modIds)
        {
            var current = _storage.ReadSettings(id);
            backup.Mods[id] = current.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value));
        }
        Directory.CreateDirectory(_library.BackupDirectory);
        var path = Path.Combine(_library.BackupDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(backup, JsonDefaults.Options));
        _logger.LogInformation("Backed up Windhawk settings to {Path}", path);
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

    /// <summary>가장 최근 백업으로 되돌리고 그 백업을 지운다 (여러 번 누르면 더 이전으로).</summary>
    public IReadOnlyList<string> RestoreLatest()
    {
        var file = LatestBackup() ?? throw new InvalidOperationException("There is nothing to restore.");
        var backup = JsonSerializer.Deserialize<BackupFile>(File.ReadAllText(file), JsonDefaults.Options)
                     ?? throw new InvalidDataException("Backup file is damaged.");
        var writes = backup.Mods
            .Where(kv => _storage.GetModState(kv.Key) != ModState.NotInstalled)
            .Select(kv => new ModSettingsWrite(kv.Key, ToSettings(kv.Value)))
            .ToList();
        _writer.Write(writes);
        File.Delete(file);
        return writes.Select(w => w.ModId).ToList();
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
