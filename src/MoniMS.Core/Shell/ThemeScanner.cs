using System.Text.Json;
using MoniMS.Core.Shell.Apps;

namespace MoniMS.Core.Shell;

/// <summary>압축 해제된 테마 폴더를 훑어서 Windhawk 설정 파일·앱 테마·배경화면·기타 항목을 찾는다.</summary>
public static class ThemeScanner
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".webp"];
    private const long MaxJsonBytes = 4 * 1024 * 1024;
    private const int MaxWallpapers = 80;

    public static void Scan(ThemePackage package)
    {
        var files = package.FilesDirectory;
        package.ContentRoot = FindContentRoot(files);
        var root = Path.Combine(files, package.ContentRoot);

        package.Mods.Clear();
        package.Wallpapers.Clear();
        package.OtherItems.Clear();
        package.Apps.Clear();

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = SafeEnumerate(root).ToList();

        foreach (var file in all.Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f))
        {
            var settings = TryReadFlatSettings(file);
            if (settings is null || settings.Count == 0)
            {
                // Windhawk 형식이 아니면 Windows Terminal 설정/색 구성표인지 확인
                if (!package.Apps.Any(a => a.Kind == AppThemeKind.WindowsTerminal) &&
                    WindowsTerminalThemer.TryLoadSource(file) is { } terminal)
                {
                    package.Apps.Add(new ThemeAppConfig
                    {
                        Kind = AppThemeKind.WindowsTerminal,
                        SourceFile = Path.GetRelativePath(files, file),
                        Summary = WindowsTerminalThemer.Summarize(terminal),
                    });
                    used.Add(Path.GetDirectoryName(file)!);
                }
                continue;
            }
            var modId = DetectMod(Path.GetFileName(file), settings);
            if (modId is null || package.Mods.Any(m => m.ModId == modId))
                continue;

            package.Mods.Add(new ThemeModConfig
            {
                ModId = modId,
                SourceFile = Path.GetRelativePath(files, file),
                SettingCount = settings.Count,
                HasCustomScript = HasCustomScript(settings),
            });
            used.Add(Path.GetDirectoryName(file)!);
        }

        // Discord 테마 (.theme.css): .theme.css를 먼저, 그다음 이름순
        var discord = all.Where(f => f.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.EndsWith(".theme.css", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(f => DiscordThemer.LooksLikeDiscordTheme(Path.GetRelativePath(root, f), f));
        if (discord is not null)
        {
            package.Apps.Add(new ThemeAppConfig
            {
                Kind = AppThemeKind.Discord,
                SourceFile = Path.GetRelativePath(files, discord),
                Summary = DiscordThemer.ReadThemeName(discord) is { } n ? $"theme '{n}'" : Path.GetFileName(discord),
            });
            used.Add(Path.GetDirectoryName(discord)!);
        }

        foreach (var file in all.Where(IsImage).Where(f => Path.GetRelativePath(root, f).Contains("wallpaper", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(MaxWallpapers))
        {
            package.Wallpapers.Add(Path.GetRelativePath(files, file));
            used.Add(Path.GetDirectoryName(file)!);
        }

        // 최상위 폴더 중 위에서 쓰지 않은 것 = 다른 앱용 설정
        foreach (var dir in System.IO.Directory.EnumerateDirectories(root).OrderBy(d => d))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.'))
                continue;
            var isUsed = used.Any(u => u.Equals(dir, StringComparison.OrdinalIgnoreCase)
                                       || u.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (!isUsed)
                package.OtherItems.Add(name);
        }

        package.Description ??= ReadReadmeTitle(root);
    }

    /// <summary>Windhawk 설정 JSON(평평한 키 → 문자열/정수)을 읽는다. 형식이 다르면 null.</summary>
    public static Dictionary<string, object>? TryReadFlatSettings(string file)
    {
        try
        {
            if (new FileInfo(file).Length > MaxJsonBytes)
                return null;
            ReadOnlyMemory<byte> bytes = File.ReadAllBytes(file);
            if (bytes.Span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
                bytes = bytes[3..]; // 메모장 등이 붙이는 UTF-8 BOM은 JsonDocument가 거부하므로 제거
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            return ToSettings(doc.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>JSON 객체 → Windhawk 설정 사전. 값이 문자열/32비트 정수가 아니면 null.</summary>
    public static Dictionary<string, object>? ToSettings(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var p in root.EnumerateObject())
        {
            object? value = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString() ?? "",
                JsonValueKind.Number when p.Value.TryGetInt32(out var i) => i,
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                _ => null,
            };
            if (value is null)
                return null; // 중첩 객체/배열 → Windhawk 설정 파일이 아님 (예: Terminal settings.json)
            result[p.Name] = value;
        }
        return result;
    }

    /// <summary>파일 이름과 내용으로 어느 Windhawk 모드의 설정인지 판단.</summary>
    public static string? DetectMod(string fileName, IReadOnlyDictionary<string, object> settings)
    {
        var keys = settings.Keys;
        if (keys.Any(k => k is "iconTheme" or "themeFolder" or "allResourceRedirect" || k.StartsWith("redirectionResourcePaths[", StringComparison.Ordinal)))
            return WindhawkMods.ResourceRedirect;

        var isStyler = keys.Any(k => k == "theme"
                                     || k.StartsWith("controlStyles[", StringComparison.Ordinal)
                                     || k.StartsWith("styleConstants[", StringComparison.Ordinal)
                                     || k.StartsWith("resourceVariables[", StringComparison.Ordinal));
        if (!isStyler)
            return null;

        var name = fileName.ToLowerInvariant();
        if (name.Contains("taskbar"))
            return WindhawkMods.TaskbarStyler;
        if (name.Contains("start"))
            return WindhawkMods.StartMenuStyler;
        if (name.Contains("notif") || name.Contains("action") || name.Contains("quick"))
            return WindhawkMods.NotificationCenterStyler;

        // 파일 이름으로 모르면 스타일 대상(target) 이름으로 판단
        var targets = string.Join("\n", settings.Where(kv => kv.Key.EndsWith(".target", StringComparison.Ordinal)).Select(kv => kv.Value));
        int Score(params string[] words) => words.Sum(w => CountOf(targets, w));
        var scores = new Dictionary<string, int>
        {
            [WindhawkMods.TaskbarStyler] = Score("Taskbar.", "SystemTray."),
            [WindhawkMods.StartMenuStyler] = Score("StartMenu.", "StartDocked.", "StartMenu"),
            [WindhawkMods.NotificationCenterStyler] = Score("ActionCenter.", "QuickActions", "NotificationCenter", "ControlCenter"),
        };
        var best = scores.MaxBy(kv => kv.Value);
        return best.Value > 0 ? best.Key : null;
    }

    public static bool HasCustomScript(IReadOnlyDictionary<string, object> settings) =>
        settings.Any(kv => kv.Key.Contains("CustomJs", StringComparison.OrdinalIgnoreCase)
                           && kv.Value is string s && !string.IsNullOrWhiteSpace(s));

    /// <summary>임의 스크립트 설정을 비운 사본.</summary>
    public static Dictionary<string, object> WithoutCustomScript(IReadOnlyDictionary<string, object> settings) =>
        settings.ToDictionary(kv => kv.Key,
            kv => kv.Key.Contains("CustomJs", StringComparison.OrdinalIgnoreCase) ? "" : kv.Value,
            StringComparer.Ordinal);

    private static int CountOf(string text, string word)
    {
        int count = 0, i = 0;
        while ((i = text.IndexOf(word, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += word.Length;
        }
        return count;
    }

    /// <summary>GitHub zip처럼 최상위에 폴더 하나만 있으면 그 안을 기준으로 삼는다.</summary>
    private static string FindContentRoot(string files)
    {
        var current = files;
        for (var depth = 0; depth < 3; depth++)
        {
            var dirs = System.IO.Directory.GetDirectories(current);
            var hasFiles = System.IO.Directory.EnumerateFiles(current).Any();
            if (dirs.Length != 1 || hasFiles)
                break;
            current = dirs[0];
        }
        return Path.GetRelativePath(files, current) is "." ? "" : Path.GetRelativePath(files, current);
    }

    private static IEnumerable<string> SafeEnumerate(string root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        return System.IO.Directory.EnumerateFiles(root, "*", options)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static bool IsImage(string file) =>
        ImageExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    private static string? ReadReadmeTitle(string root)
    {
        var readme = System.IO.Directory.EnumerateFiles(root, "README*", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (readme is null)
            return null;
        try
        {
            var line = File.ReadLines(readme).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith('#'));
            return line?.TrimStart('#').Trim();
        }
        catch (IOException)
        {
            return null;
        }
    }
}
