using System.Text.RegularExpressions;

namespace MoniMS.Core.Shell;

/// <summary>
/// Windhawk 스타일 설정 안의 이미지 주소를 이 PC의 파일로 바꾼다.
/// 테마들은 시작 버튼 그림 같은 이미지를 GitHub 주소(raw.githubusercontent.com)로 넣어 두는데,
/// 탐색기가 그 주소를 못 받으면(인터넷·DNS 문제, 부팅 직후 등) 그림이 비어 버린다.
/// 같은 이름의 파일이 테마 패키지 안에 있으면 그 파일을 복사해 두고 로컬 경로를 쓴다.
/// </summary>
public static partial class ThemeAssets
{
    [GeneratedRegex("""ImageSource\s*=\s*(?<q>["'])(?<url>https?://[^"']+)\k<q>""", RegexOptions.IgnoreCase)]
    private static partial Regex ImageUrlRegex();

    /// <summary>설정 값 안의 원격 이미지 주소 목록.</summary>
    public static IEnumerable<string> FindImageUrls(IReadOnlyDictionary<string, object> settings) =>
        settings.Values.OfType<string>()
            .SelectMany(v => ImageUrlRegex().Matches(v).Select(m => m.Groups["url"].Value))
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// 패키지 안에 있는 이미지는 assetDirectory로 복사하고 설정의 주소를 그 파일 경로로 바꾼 사본을 돌려준다.
    /// 바꾼 개수는 localized로.
    /// </summary>
    public static Dictionary<string, object> LocalizeImages(IReadOnlyDictionary<string, object> settings,
        string packageFilesDirectory, string assetDirectory, out int localized)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var url in FindImageUrls(settings))
        {
            var local = FindInPackage(url, packageFilesDirectory);
            if (local is null)
                continue;
            Directory.CreateDirectory(assetDirectory);
            var dest = Path.Combine(assetDirectory, Path.GetFileName(local));
            File.Copy(local, dest, overwrite: true);
            replacements[url] = dest;
        }

        localized = replacements.Count;
        if (replacements.Count == 0)
            return new Dictionary<string, object>(settings, StringComparer.Ordinal);

        return settings.ToDictionary(kv => kv.Key, kv => kv.Value is string s
            ? ImageUrlRegex().Replace(s, m => replacements.TryGetValue(m.Groups["url"].Value, out var path)
                ? $"ImageSource={m.Groups["q"].Value}{path}{m.Groups["q"].Value}"
                : m.Value)
            : kv.Value, StringComparer.Ordinal);
    }

    /// <summary>주소의 파일 이름과 같은 파일을 패키지에서 찾는다. 여러 개면 주소의 경로 끝부분과 가장 많이 겹치는 것.</summary>
    internal static string? FindInPackage(string url, string packageFilesDirectory)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !Directory.Exists(packageFilesDirectory))
            return null;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        if (segments.Length == 0)
            return null;
        var name = segments[^1];
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, MatchCasing = MatchCasing.CaseInsensitive };
        var candidates = Directory.EnumerateFiles(packageFilesDirectory, name, options).ToList();
        if (candidates.Count <= 1)
            return candidates.FirstOrDefault();

        int Overlap(string file)
        {
            var parts = Path.GetRelativePath(packageFilesDirectory, file).Split(Path.DirectorySeparatorChar);
            var n = 0;
            while (n < parts.Length && n < segments.Length &&
                   string.Equals(parts[^(n + 1)], segments[^(n + 1)], StringComparison.OrdinalIgnoreCase))
                n++;
            return n;
        }
        return candidates.OrderByDescending(Overlap).First();
    }
}
