using System.Text.Json;
using MoniMS.Core.Presets;

namespace MoniMS.Core.Shell;

/// <summary>가져온 테마 보관함: %APPDATA%\MoniMS\themes\{id}\theme.json + files\</summary>
public sealed class ThemeLibrary
{
    private const string ManifestName = "theme.json";

    public ThemeLibrary(string? root = null)
    {
        RootDirectory = root ?? Path.Combine(AppPaths.DataDirectory, "themes");
        Directory.CreateDirectory(RootDirectory);
    }

    public string RootDirectory { get; }

    public string BackupDirectory => Path.Combine(RootDirectory, "_backups");

    public IReadOnlyList<ThemePackage> GetAll()
    {
        var list = new List<ThemePackage>();
        foreach (var dir in Directory.EnumerateDirectories(RootDirectory))
        {
            if (Path.GetFileName(dir).StartsWith('_'))
                continue;
            var p = Load(dir);
            if (p is not null)
                list.Add(p);
        }
        return list.OrderByDescending(p => p.ImportedAt).ToList();
    }

    public static ThemePackage? Load(string directory)
    {
        var path = Path.Combine(directory, ManifestName);
        if (!File.Exists(path))
            return null;
        try
        {
            var p = JsonSerializer.Deserialize<ThemePackage>(File.ReadAllText(path), JsonDefaults.Options);
            if (p is null)
                return null;
            p.Directory = directory;
            if (p.SchemaVersion < ThemePackage.CurrentSchemaVersion && Directory.Exists(p.FilesDirectory))
            {
                // 예전 버전에서 가져온 테마: 새로 지원하는 항목(앱 테마 등)을 찾도록 다시 분석
                ThemeScanner.Scan(p);
                p.SchemaVersion = ThemePackage.CurrentSchemaVersion;
                TrySave(p);
            }
            return p;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TrySave(ThemePackage package)
    {
        try
        {
            Save(package);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 저장 못 해도 이번 실행에서는 분석 결과를 그대로 쓴다
        }
    }

    public ThemePackage Create(string name, string source)
    {
        var slug = new string(name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-');
        if (slug.Length == 0)
            slug = "theme";
        if (slug.Length > 40)
            slug = slug[..40];
        var id = $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";
        var dir = Path.Combine(RootDirectory, id);
        Directory.CreateDirectory(Path.Combine(dir, "files"));
        return new ThemePackage { Id = id, Name = name, Source = source, Directory = dir };
    }

    public static void Save(ThemePackage package)
    {
        var path = Path.Combine(package.Directory, ManifestName);
        File.WriteAllText(path, JsonSerializer.Serialize(package, JsonDefaults.Options));
    }

    public void Delete(ThemePackage package)
    {
        // 보관함 밖을 지우는 일이 없도록 경로 확인
        var full = Path.GetFullPath(package.Directory);
        if (!full.StartsWith(Path.GetFullPath(RootDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to delete outside the theme library.");
        if (Directory.Exists(full))
            Directory.Delete(full, recursive: true);
    }
}
