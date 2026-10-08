using System.Text.RegularExpressions;

namespace MoniMS.Core.Shell.Apps;

/// <summary>
/// YASB(상태 표시줄) 테마 적용. YASB 테마는 막대 구성(config.yaml)과 모양(styles.css)이 서로 짝이라서
/// 두 파일을 함께 YASB 설정 폴더(%USERPROFILE%\.config\yasb 또는 YASB_CONFIG_HOME)에 넣는다.
/// YASB는 파일이 바뀌면 스스로 다시 불러온다 (watch_config / watch_stylesheet 기본값).
/// </summary>
public static partial class YasbThemer
{
    public const string ConfigFileName = "config.yaml";
    public const string StylesFileName = "styles.css";
    private const long MaxBytes = 2 * 1024 * 1024;

    /// <param name="userProfile">테스트용 사용자 폴더. null이면 실제 위치 (YASB_CONFIG_HOME 우선).</param>
    public static string ConfigDirectory(string? userProfile = null)
    {
        if (userProfile is null && Environment.GetEnvironmentVariable("YASB_CONFIG_HOME") is { Length: > 0 } home)
            return home;
        return Path.Combine(userProfile ?? AppLocator.UserProfile, ".config", "yasb");
    }

    /// <summary>설치돼 있는지: 설정 폴더(처음 실행할 때 생김), 실행 중, 설치 폴더.</summary>
    public static bool IsInstalled(string? userProfile = null)
    {
        if (Directory.Exists(ConfigDirectory(userProfile)))
            return true;
        if (userProfile is not null)
            return false;
        return AppLocator.IsRunning("yasb")
               || AppLocator.FindExecutable("yasb.exe", AppLocator.ProgramFiles("YASB"), AppLocator.LocalPrograms("YASB")) is not null;
    }

    /// <summary>YASB 설정 파일인지: 최상위에 bars: 와 widgets: 가 있는 yaml.</summary>
    public static bool IsConfig(string file)
    {
        var ext = Path.GetExtension(file);
        if (!ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".yml", StringComparison.OrdinalIgnoreCase))
            return false;
        var text = ReadSmall(file);
        return text is not null && BarsRegex().IsMatch(text) && WidgetsRegex().IsMatch(text);
    }

    /// <summary>YASB 스타일인지: 막대의 기본 클래스(.yasb-bar)를 꾸미는 css.</summary>
    public static bool IsStylesheet(string file) =>
        file.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
        && ReadSmall(file)?.Contains(".yasb-bar", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>한 폴더 안의 YASB 설정·스타일 파일 (이름이 정확히 config.yaml / styles.css가 아니어도 찾는다).</summary>
    public static (string? Config, string? Styles) FilesIn(string directory)
    {
        var files = Directory.GetFiles(directory);
        var config = files.Where(IsConfig)
            .OrderBy(f => Path.GetFileName(f).Equals(ConfigFileName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        var styles = files.Where(IsStylesheet)
            .OrderBy(f => Path.GetFileName(f).ToLowerInvariant() switch { StylesFileName => 0, "style.css" => 1, _ => 2 })
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return (config, styles);
    }

    public static string Summarize(string? config, string? styles) => (config, styles) switch
    {
        (not null, not null) => "bar layout + styles",
        (not null, null) => "bar layout only",
        _ => "styles only",
    };

    /// <summary>
    /// <paramref name="sourceFile"/>가 있는 폴더의 config/styles를 YASB 설정 폴더에 넣는다 (바꾸기 전 내용은 journal에).
    /// 넣은 파일 수 반환.
    /// </summary>
    public static int Apply(string sourceFile, FileJournal journal, List<string> warnings,
        string? userProfile = null, Func<string, bool>? fontInstalled = null)
    {
        var (config, styles) = FilesIn(Path.GetDirectoryName(sourceFile)!);
        if (config is null && styles is null)
            throw new InvalidDataException("Not a YASB theme.");

        var target = ConfigDirectory(userProfile);
        Directory.CreateDirectory(target);
        var count = 0;
        foreach (var (from, name) in new[] { (config, ConfigFileName), (styles, StylesFileName) })
        {
            if (from is null)
                continue;
            var dest = Path.Combine(target, name);
            journal.Record(dest);
            File.Copy(from, dest, overwrite: true);
            count++;
        }

        if (styles is not null)
        {
            fontInstalled ??= FontRegistry.IsInstalled;
            foreach (var font in CssFonts.Read(styles).Where(f => !fontInstalled(f)))
                warnings.Add($"YASB: font '{font}' is not installed, YASB will use a fallback font.");
        }
        if (config is null)
            warnings.Add("YASB: the theme has no config.yaml, kept your bar layout. Some styles may not match your widgets.");
        return count;
    }

    private static string? ReadSmall(string file)
    {
        try
        {
            return new FileInfo(file).Length > MaxBytes ? null : File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^bars\s*:", RegexOptions.Multiline)]
    private static partial Regex BarsRegex();

    [GeneratedRegex(@"^widgets\s*:", RegexOptions.Multiline)]
    private static partial Regex WidgetsRegex();
}
