using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MoniMS.Core.Presets;

/// <summary>
/// 저장 구조: {root}/{presetId}/preset.json + 배경화면 이미지 복사본.
/// 폴더 단위라서 프리셋을 통째로 공유/백업하기 쉽다.
/// </summary>
public sealed class PresetStore : IPresetStore
{
    private const string FileName = "preset.json";
    private readonly ILogger<PresetStore> _logger;

    public PresetStore(string? rootDirectory = null, ILogger<PresetStore>? logger = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(AppPaths.DataDirectory, "presets");
        Directory.CreateDirectory(RootDirectory);
        _logger = logger ?? NullLogger<PresetStore>.Instance;
    }

    public string RootDirectory { get; }

    public IReadOnlyList<Preset> GetAll()
    {
        var list = new List<Preset>();
        foreach (var dir in Directory.EnumerateDirectories(RootDirectory))
        {
            var preset = Load(Path.Combine(dir, FileName));
            if (preset is not null)
                list.Add(preset);
        }
        return list.OrderBy(p => p.CreatedAt).ToList();
    }

    public Preset? Get(string id) => Load(Path.Combine(RootDirectory, Sanitize(id), FileName));

    public void Save(Preset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        preset.UpdatedAt = DateTimeOffset.Now;
        var path = Path.Combine(GetPresetDirectory(preset.Id), FileName);

        // 쓰다가 꺼져도 기존 파일이 깨지지 않게 임시 파일에 먼저 쓰고 교체
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(preset, JsonDefaults.Options));
        File.Move(tmp, path, overwrite: true);
    }

    public void Delete(string id)
    {
        var dir = Path.Combine(RootDirectory, Sanitize(id));
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    public string GetPresetDirectory(string id)
    {
        var dir = Path.Combine(RootDirectory, Sanitize(id));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private Preset? Load(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Preset>(File.ReadAllText(path), JsonDefaults.Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Failed to read preset: {Path}", path);
            return null;
        }
    }

    /// <summary>경로 조작(../ 등) 방지.</summary>
    internal static string Sanitize(string id)
    {
        var clean = new string(id.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (string.IsNullOrEmpty(clean))
            throw new ArgumentException("Invalid preset id", nameof(id));
        return clean;
    }
}
