using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MoniMS.Core.Shell.Apps;

/// <summary>
/// komorebi(타일링 창 관리자) 테마 적용.
/// 내 komorebi.json이 있으면 "모양" 설정(색 테마, 테두리, 여백, 투명도, 애니메이션)만 합치고
/// 모니터·작업 공간·규칙 같은 개인 설정은 그대로 둔다. 아직 없으면 테마의 komorebi.json을 그대로 쓴다.
/// whkd 단축키(whkdrc)는 아직 없을 때만 넣는다 (있으면 내 단축키 유지).
/// </summary>
public static partial class KomorebiThemer
{
    public const string ConfigFileName = "komorebi.json";

    /// <summary>komorebi.json에서 가져오는 "모양" 관련 키.</summary>
    internal static readonly string[] AppearanceKeys =
    [
        "theme", "border", "border_colours", "border_implementation", "border_offset", "border_style", "border_width",
        "default_container_padding", "default_workspace_padding", "transparency", "transparency_alpha", "animation", "stackbar",
    ];

    /// <summary>테마 폴더 안의 whkd 단축키 파일 이름 (dotfiles에서는 확장자 없이 "whkd"로 올리기도 한다).</summary>
    private static readonly string[] WhkdFileNames = ["whkdrc", "whkd"];

    /// <summary>komorebi.json에만 있는 키 (다른 JSON과 구별용).</summary>
    private static readonly string[] SignatureKeys =
    [
        "monitors", "default_workspace_padding", "default_container_padding", "window_hiding_behaviour",
        "app_specific_configuration_path", "border_colours", "stackbar", "cross_monitor_move_behaviour",
    ];

    /// <param name="userProfile">테스트용 사용자 폴더. null이면 실제 위치 (KOMOREBI_CONFIG_HOME 우선).</param>
    public static string ConfigDirectory(string? userProfile = null) =>
        userProfile ?? (Environment.GetEnvironmentVariable("KOMOREBI_CONFIG_HOME") is { Length: > 0 } home ? home : AppLocator.UserProfile);

    /// <summary>whkd 설정 폴더 (기본 %USERPROFILE%\.config, WHKD_CONFIG_HOME 우선).</summary>
    public static string WhkdDirectory(string? userProfile = null) =>
        userProfile is null && Environment.GetEnvironmentVariable("WHKD_CONFIG_HOME") is { Length: > 0 } home
            ? home
            : Path.Combine(userProfile ?? AppLocator.UserProfile, ".config");

    public static bool IsInstalled(string? userProfile = null)
    {
        if (File.Exists(Path.Combine(ConfigDirectory(userProfile), ConfigFileName)))
            return true;
        if (userProfile is not null)
            return false;
        return AppLocator.IsRunning("komorebi") || FindKomorebic() is not null;
    }

    private static string? FindKomorebic() =>
        AppLocator.FindExecutable("komorebic.exe", AppLocator.ProgramFiles("komorebi", "bin"));

    /// <summary>komorebi.json 형식인지 (komorebi-bar 설정은 제외).</summary>
    public static bool IsConfig(string file, JsonObject root)
    {
        var schema = root["$schema"] is JsonValue v && v.TryGetValue(out string? s) ? s : "";
        if (Path.GetFileName(file).Contains("bar", StringComparison.OrdinalIgnoreCase) || schema.Contains("bar", StringComparison.OrdinalIgnoreCase))
            return false;
        if (schema.Contains("komorebi", StringComparison.OrdinalIgnoreCase))
            return true;
        return SignatureKeys.Count(root.ContainsKey) >= 2;
    }

    public static string Summarize(JsonObject root)
    {
        if (root["theme"] is JsonObject theme)
        {
            var name = string.Join(" ", new[] { theme["palette"], theme["name"] }
                .Select(n => n is JsonValue jv && jv.TryGetValue(out string? t) ? t : null).OfType<string>());
            if (name.Length > 0)
                return $"theme '{name}'";
        }
        return "borders, padding & transparency";
    }

    /// <summary>source의 모양 설정을 target(내 komorebi.json)에 합친다. 순수 함수라 테스트 가능.</summary>
    public static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var key in AppearanceKeys)
        {
            if (source[key] is { } value)
                target[key] = value.DeepClone();
        }
    }

    /// <summary>
    /// komorebi.json(과 whkdrc) 적용. <paramref name="reload"/>면 실행 중인 komorebi에 새 설정을 불러오게 한다.
    /// 적용했으면 1, 아니면 0.
    /// </summary>
    public static int Apply(string sourceFile, FileJournal journal, List<string> warnings,
        string? userProfile = null, bool reload = false)
    {
        var source = JsonText.ParseFile(sourceFile) as JsonObject;
        if (source is null || !IsConfig(sourceFile, source))
            throw new InvalidDataException("Not a komorebi.json file.");

        var dir = ConfigDirectory(userProfile);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, ConfigFileName);
        if (File.Exists(path))
        {
            var target = JsonText.ParseFile(path) as JsonObject
                         ?? throw new InvalidDataException("Your komorebi.json could not be read.");
            Merge(target, source);
            journal.Record(path);
            JsonText.Write(path, target);
        }
        else
        {
            journal.Record(path);
            File.Copy(sourceFile, path);
            WarnIfAppSpecificConfigMissing(source, userProfile, warnings);
        }

        // whkd 단축키: 아직 없을 때만 (있으면 내 단축키 유지)
        var whkdSource = WhkdFileNames
            .Select(n => Path.Combine(Path.GetDirectoryName(sourceFile)!, n))
            .FirstOrDefault(File.Exists);
        if (whkdSource is not null)
        {
            var whkdPath = Path.Combine(WhkdDirectory(userProfile), "whkdrc");
            if (!File.Exists(whkdPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(whkdPath)!);
                journal.Record(whkdPath);
                File.Copy(whkdSource, whkdPath);
            }
        }

        if (reload)
            Reload(path, warnings);
        return 1;
    }

    /// <summary>
    /// 실행 중인 komorebi에 설정 파일을 다시 불러오게 한다 (komorebic replace-configuration).
    /// komorebi가 꺼져 있으면 다음에 켤 때 읽으므로 아무것도 안 한다.
    /// </summary>
    public static void Reload(string configPath, List<string>? warnings = null)
    {
        if (!AppLocator.IsRunning("komorebi"))
            return;
        var komorebic = FindKomorebic();
        var ok = false;
        if (komorebic is not null)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(komorebic)
                {
                    ArgumentList = { "replace-configuration", configPath },
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
                ok = p is not null && p.WaitForExit(10_000) && p.ExitCode == 0;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                ok = false;
            }
        }
        if (!ok)
            warnings?.Add("Komorebi: restart komorebi to load the new settings.");
    }

    /// <summary>
    /// 테마 설정이 가리키는 applications.json(앱별 예외 설정)이 이 PC에 없으면 받는 방법을 알려준다.
    /// </summary>
    private static void WarnIfAppSpecificConfigMissing(JsonObject source, string? userProfile, List<string> warnings)
    {
        var paths = source["app_specific_configuration_path"] switch
        {
            JsonValue v when v.TryGetValue(out string? s) => [s],
            JsonArray a => a.Select(n => n is JsonValue jv && jv.TryGetValue(out string? s) ? s : null).OfType<string>().ToList(),
            _ => new List<string>(),
        };
        bool Missing(string p)
        {
            try
            {
                return !File.Exists(ExpandPath(p, userProfile));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false; // 해석할 수 없는 경로는 komorebi가 알아서 알린다
            }
        }

        if (paths.Any(Missing))
            warnings.Add("Komorebi: applications.json is missing. Run 'komorebic fetch-app-specific-configuration' to download it.");
    }

    /// <summary>"$Env:USERPROFILE/x", "$KOMOREBI_CONFIG_HOME/x", "%USERPROFILE%\x", "~/x" 를 실제 경로로.</summary>
    internal static string ExpandPath(string path, string? userProfile = null)
    {
        string Lookup(string name)
        {
            if (userProfile is not null && name.ToUpperInvariant() is "USERPROFILE" or "KOMOREBI_CONFIG_HOME" or "HOME")
                return userProfile;
            return Environment.GetEnvironmentVariable(name)
                   ?? (name.Equals("KOMOREBI_CONFIG_HOME", StringComparison.OrdinalIgnoreCase) ? ConfigDirectory() : "");
        }

        var expanded = EnvVarRegex().Replace(path, m => Lookup(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value));
        if (expanded.StartsWith('~'))
            expanded = (userProfile ?? AppLocator.UserProfile) + expanded[1..];
        return Path.GetFullPath(expanded.Replace('/', Path.DirectorySeparatorChar));
    }

    [GeneratedRegex(@"\$Env:(\w+)|\$(\w+)|%(\w+)%", RegexOptions.IgnoreCase)]
    private static partial Regex EnvVarRegex();
}
