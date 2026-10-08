using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MoniMS.Core.Shell.Apps;

/// <summary>설치된 프로그램 찾기 (PATH, 흔한 설치 폴더, 실행 중인 프로세스).</summary>
internal static class AppLocator
{
    /// <summary>PATH와 주어진 폴더에서 실행 파일을 찾는다. 없으면 null.</summary>
    public static string? FindExecutable(string exeName, params string[] extraDirectories)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Concat(extraDirectories))
        {
            try
            {
                var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(dir.Trim('"')), exeName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // PATH에 잘못된 문자가 든 항목
            }
        }
        return null;
    }

    public static bool IsRunning(string processName)
    {
        var ps = Process.GetProcessesByName(processName);
        foreach (var p in ps)
            p.Dispose();
        return ps.Length > 0;
    }

    public static string ProgramFiles(params string[] parts) =>
        Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), .. parts]);

    public static string LocalPrograms(params string[] parts) =>
        Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", .. parts]);

    public static string UserProfile =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}

/// <summary>CSS가 쓰는 글꼴 이름 (font-family, --font / --system-font 같은 변수).</summary>
public static partial class CssFonts
{
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "ui-monospace", "ui-sans-serif",
        "ui-serif", "emoji", "math", "inherit", "initial", "unset", "revert", "default",
    };

    public static IReadOnlyList<string> Read(string file)
    {
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var result = new List<string>();
        foreach (Match m in DeclarationRegex().Matches(text))
        {
            if (NotFamilyRegex().IsMatch(m.Groups[1].Value))
                continue; // --font-size, --font-weight 같은 변수
            foreach (var part in m.Groups[2].Value.Split(','))
            {
                var name = part.Trim().Trim('"', '\'').Trim();
                if (name.Length == 0 || !char.IsLetter(name[0]) || name.Contains('(') || Generic.Contains(name)
                    || result.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;
                result.Add(name);
            }
        }
        return result;
    }

    // font-family: "A", 'B', sans-serif;   --font-bar: "A";   --system-font: 'A'  (var(...) 참조는 제외)
    [GeneratedRegex(@"(font-family|--[\w-]*font[\w-]*)\s*:\s*([^;{}!]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DeclarationRegex();

    [GeneratedRegex("size|weight|style|colou?r|spacing|height|variant|stretch|feature", RegexOptions.IgnoreCase)]
    private static partial Regex NotFamilyRegex();
}
