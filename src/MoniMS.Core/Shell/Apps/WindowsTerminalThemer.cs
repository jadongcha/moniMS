using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace MoniMS.Core.Shell.Apps;

/// <summary>
/// Windows Terminal 테마 적용. 패키지의 settings.json 전체를 덮어쓰지 않고
/// "색 구성표(schemes) + 모양 관련 기본값(profiles.defaults)"만 내 settings.json에 합친다.
/// 단축키·기본 프로필·프로필 목록 같은 개인 설정은 건드리지 않는다.
/// </summary>
public static class WindowsTerminalThemer
{
    /// <summary>profiles.defaults 에서 가져오는 "모양" 관련 키.</summary>
    private static readonly string[] AppearanceKeys =
    [
        "colorScheme", "opacity", "useAcrylic", "padding", "cursorShape", "cursorHeight",
        "intenseTextStyle", "adjustIndistinguishableColors", "backgroundImage", "backgroundImageOpacity",
        "backgroundImageStretchMode", "backgroundImageAlignment", "unfocusedAppearance", "font",
        "fontFace", "fontSize", "fontWeight",
    ];

    private static readonly string[] GlobalAppearanceKeys = ["useAcrylicInTabRow"];

    private static readonly string[] PackageFolders =
    [
        "Microsoft.WindowsTerminal_8wekyb3d8bbwe",
        "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe",
    ];

    /// <summary>설치된 Windows Terminal의 settings.json 경로들 (아직 한 번도 안 켰으면 만들 위치).</summary>
    public static IReadOnlyList<string> FindSettingsFiles(string? localAppData = null)
    {
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var result = new List<string>();
        foreach (var pkg in PackageFolders)
        {
            var pkgDir = Path.Combine(localAppData, "Packages", pkg);
            if (Directory.Exists(pkgDir))
                result.Add(Path.Combine(pkgDir, "LocalState", "settings.json"));
        }
        var unpackaged = Path.Combine(localAppData, "Microsoft", "Windows Terminal", "settings.json");
        if (File.Exists(unpackaged))
            result.Add(unpackaged);
        return result;
    }

    /// <summary>테마 파일이 Terminal 설정(또는 색 구성표 하나)인지 판별하고, 표준 형태로 바꿔서 반환.</summary>
    public static JsonObject? TryLoadSource(string file)
    {
        try
        {
            if (new FileInfo(file).Length > 4 * 1024 * 1024)
                return null;
            return Normalize(JsonText.ParseFile(file));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static JsonObject? Normalize(JsonNode? node)
    {
        if (node is not JsonObject root)
            return null;

        // 1) settings.json 형태: "schemes" 배열이 있음
        if (root["schemes"] is JsonArray schemes && schemes.OfType<JsonObject>().Any(IsScheme))
            return root;

        // 2) 색 구성표 하나만 공유된 형태 (windowsterminalthemes.dev 등)
        if (IsScheme(root))
        {
            return new JsonObject
            {
                ["schemes"] = new JsonArray(root.DeepClone()),
                ["profiles"] = new JsonObject { ["defaults"] = new JsonObject { ["colorScheme"] = root["name"]!.GetValue<string>() } },
            };
        }
        return null;
    }

    private static bool IsScheme(JsonObject o) =>
        o["name"] is JsonValue && o["background"] is JsonValue && o["foreground"] is JsonValue && o["black"] is JsonValue;

    /// <summary>패키지가 기본으로 쓰려는 색 구성표 이름.</summary>
    public static string? PreferredScheme(JsonObject source)
    {
        var profiles = source["profiles"];
        if (profiles is JsonObject po && po["defaults"] is JsonObject d && d["colorScheme"] is JsonValue v && v.TryGetValue(out string? s))
            return s;
        // defaults에 없으면 프로필들이 가장 많이 쓰는 구성표
        var list = profiles is JsonObject p2 ? p2["list"] as JsonArray : profiles as JsonArray;
        var fromList = list?.OfType<JsonObject>()
            .Select(x => x["colorScheme"] is JsonValue cv && cv.TryGetValue(out string? cs) ? cs : null)
            .Where(x => !string.IsNullOrEmpty(x)).GroupBy(x => x).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
        return fromList ?? (source["schemes"] as JsonArray)?.OfType<JsonObject>().Select(x => x["name"]?.GetValue<string>()).FirstOrDefault();
    }

    public static string Summarize(JsonObject source)
    {
        var scheme = PreferredScheme(source);
        var count = (source["schemes"] as JsonArray)?.Count ?? 0;
        var extra = count > 1 ? $" (+{count - 1} more schemes)" : "";
        return scheme is null ? $"{count} color schemes" : $"color scheme '{scheme}'{extra}";
    }

    /// <summary>
    /// source의 색 구성표·모양 설정을 target(내 settings.json)에 합친다. 순수 함수라 테스트 가능.
    /// </summary>
    public static void Merge(JsonObject target, JsonObject source, Func<string, bool> fontInstalled, List<string> warnings)
    {
        MergeNamedArray(target, source, "schemes");
        MergeNamedArray(target, source, "themes");

        // 패키지가 자체 창 테마(themes)를 정의하고 그걸 쓰도록 했으면 따라감
        if (source["theme"] is JsonValue tv && tv.TryGetValue(out string? themeName) &&
            (source["themes"] as JsonArray)?.OfType<JsonObject>().Any(t => t["name"]?.GetValue<string>() == themeName) == true)
            target["theme"] = themeName;

        foreach (var key in GlobalAppearanceKeys)
        {
            if (source[key] is { } v)
                target[key] = v.DeepClone();
        }

        // profiles: 예전 형식(배열)이면 {defaults, list}로 바꿈
        var profiles = target["profiles"] switch
        {
            JsonObject o => o,
            JsonArray a => new JsonObject { ["defaults"] = new JsonObject(), ["list"] = a.DeepClone() },
            _ => new JsonObject { ["defaults"] = new JsonObject(), ["list"] = new JsonArray() },
        };
        target["profiles"] = profiles;
        if (profiles["defaults"] is not JsonObject defaults)
        {
            defaults = new JsonObject();
            profiles["defaults"] = defaults;
        }

        var srcDefaults = (source["profiles"] as JsonObject)?["defaults"] as JsonObject;
        if (srcDefaults is not null)
        {
            foreach (var key in AppearanceKeys)
            {
                if (srcDefaults[key] is not { } value)
                    continue;
                var copy = value.DeepClone();

                if (key == "font" && copy is JsonObject font && font["face"] is JsonValue fv && fv.TryGetValue(out string? face))
                {
                    if (!fontInstalled(face))
                    {
                        font.Remove("face");
                        warnings.Add($"Windows Terminal: font '{face}' is not installed, kept your current font.");
                        if (font.Count == 0)
                            continue;
                    }
                }
                if (key == "fontFace" && copy is JsonValue ff && ff.TryGetValue(out string? legacyFace) && !fontInstalled(legacyFace))
                {
                    warnings.Add($"Windows Terminal: font '{legacyFace}' is not installed, kept your current font.");
                    continue;
                }
                if (key == "backgroundImage" && copy is JsonValue bv && bv.TryGetValue(out string? img) &&
                    img != "desktopWallpaper" && !File.Exists(Environment.ExpandEnvironmentVariables(img)))
                {
                    warnings.Add("Windows Terminal: the theme's background image is not on this PC, skipped.");
                    continue;
                }

                // font는 내 설정의 크기 등은 유지하고 패키지 값만 덮어씀
                if (key == "font" && copy is JsonObject newFont && defaults["font"] is JsonObject oldFont)
                {
                    foreach (var (k, v) in newFont.ToList())
                        oldFont[k] = v?.DeepClone();
                    continue;
                }
                defaults[key] = copy;
            }
        }

        var scheme = PreferredScheme(source);
        if (scheme is not null)
            defaults["colorScheme"] = scheme;

        // 프로필마다 따로 색 구성표가 지정돼 있으면 기본값이 안 먹힘 → 알려줌
        if (profiles["list"] is JsonArray list)
        {
            var own = list.OfType<JsonObject>()
                .Where(p => p["colorScheme"] is JsonValue cv && cv.TryGetValue(out string? cs) && cs != scheme)
                .Select(p => p["name"]?.GetValue<string>() ?? "?").ToList();
            if (own.Count > 0)
                warnings.Add($"Windows Terminal: these profiles use their own color scheme: {string.Join(", ", own)}.");
        }
    }

    private static void MergeNamedArray(JsonObject target, JsonObject source, string key)
    {
        if (source[key] is not JsonArray items || items.Count == 0)
            return;
        if (target[key] is not JsonArray existing)
        {
            existing = new JsonArray();
            target[key] = existing;
        }
        foreach (var item in items.OfType<JsonObject>())
        {
            var name = item["name"]?.GetValue<string>();
            if (name is null)
                continue;
            var index = existing.Select((n, i) => (n, i))
                .FirstOrDefault(t => t.n is JsonObject o && string.Equals(o["name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase)).i;
            var match = existing.Count > 0 && existing[index] is JsonObject m &&
                        string.Equals(m["name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase);
            if (match)
                existing[index] = item.DeepClone();
            else
                existing.Add(item.DeepClone());
        }
    }

    /// <summary>찾은 모든 Terminal settings.json에 적용. 적용한 파일 수 반환.</summary>
    public static int Apply(string sourceFile, FileJournal journal, List<string> warnings,
        Func<string, bool>? fontInstalled = null, string? localAppData = null)
    {
        var source = TryLoadSource(sourceFile) ?? throw new InvalidDataException("Not a Windows Terminal theme file.");
        fontInstalled ??= FontRegistry.IsInstalled;
        var files = FindSettingsFiles(localAppData);
        if (files.Count == 0)
        {
            warnings.Add("Windows Terminal is not installed, skipped.");
            return 0;
        }

        foreach (var path in files)
        {
            var target = (File.Exists(path) ? JsonText.ParseFile(path) : null) as JsonObject ?? new JsonObject
            {
                ["$schema"] = "https://aka.ms/terminal-profiles-schema",
            };
            var fileWarnings = new List<string>();
            Merge(target, source, fontInstalled, fileWarnings);
            foreach (var w in fileWarnings.Where(w => !warnings.Contains(w)))
                warnings.Add(w);
            journal.Record(path);
            JsonText.Write(path, target); // Terminal이 파일 변경을 감지해 바로 반영
        }
        return files.Count;
    }
}

/// <summary>설치된 글꼴 확인 (Windows 글꼴 레지스트리: 시스템 + 사용자별 설치).</summary>
public static class FontRegistry
{
    private const string FontsKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";
    private static HashSet<string>? _names;

    public static bool IsInstalled(string family)
    {
        if (string.IsNullOrWhiteSpace(family))
            return true;
        _names ??= Load();
        // 값 이름 예: "JetBrainsMono Nerd Font Regular (TrueType)" → 앞부분이 패밀리 이름과 같으면 설치된 것
        return _names.Any(n => n.StartsWith(family, StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> Load()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = hive.OpenSubKey(FontsKey);
                foreach (var name in key?.GetValueNames() ?? [])
                    set.Add(name);
            }
            catch (System.Security.SecurityException)
            {
            }
        }
        return set;
    }
}
