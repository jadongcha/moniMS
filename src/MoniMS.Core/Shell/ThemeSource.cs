namespace MoniMS.Core.Shell;

public enum ThemeSourceKind
{
    LocalFolder,
    LocalZip,
    LocalJson,
    RemoteZip,
    RemoteFile,
}

/// <summary>사용자가 붙여넣은 주소/경로를 실제로 내려받을 대상으로 해석한 결과.</summary>
public sealed record ThemeSource(ThemeSourceKind Kind, string Location, string SuggestedName, string? SubPath = null)
{
    /// <summary>
    /// 지원 형식:
    /// <list type="bullet">
    /// <item>로컬 폴더 / .zip / .json</item>
    /// <item>github.com/{owner}/{repo} → 기본 브랜치 zip</item>
    /// <item>github.com/{owner}/{repo}/tree/{branch}[/{path}] → 해당 브랜치 zip (+하위 폴더)</item>
    /// <item>github.com/{owner}/{repo}/blob/{branch}/{path}.json → raw 파일</item>
    /// <item>그 외 http(s) 링크 → 받아서 zip/json 판별</item>
    /// </list>
    /// </summary>
    public static ThemeSource Parse(string input)
    {
        var s = input.Trim().Trim('"');
        if (string.IsNullOrEmpty(s))
            throw new ArgumentException("Enter a link or a path.");

        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return ParseUrl(uri);

        if (System.IO.Directory.Exists(s))
            return new ThemeSource(ThemeSourceKind.LocalFolder, s, Path.GetFileName(s.TrimEnd('\\', '/')));
        if (File.Exists(s))
        {
            var ext = Path.GetExtension(s).ToLowerInvariant();
            return ext switch
            {
                ".zip" => new ThemeSource(ThemeSourceKind.LocalZip, s, Path.GetFileNameWithoutExtension(s)),
                ".json" => new ThemeSource(ThemeSourceKind.LocalJson, s, Path.GetFileNameWithoutExtension(s)),
                _ => throw new NotSupportedException("Only .zip, .json or folders are supported."),
            };
        }

        // "github.com/owner/repo" 처럼 스킴 없이 붙여넣은 경우
        if (s.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase) || s.StartsWith("www.github.com/", StringComparison.OrdinalIgnoreCase))
            return ParseUrl(new Uri("https://" + s));

        throw new FileNotFoundException("Path or link not found.", s);
    }

    private static ThemeSource ParseUrl(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();

        if (host is "github.com" or "www.github.com" && parts.Length >= 2)
        {
            var owner = parts[0];
            var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
            var name = repo;

            if (parts.Length >= 4 && parts[2] == "blob")
            {
                var raw = $"https://raw.githubusercontent.com/{owner}/{repo}/{parts[3]}/{string.Join('/', parts[4..])}";
                return new ThemeSource(ThemeSourceKind.RemoteFile, raw, Path.GetFileNameWithoutExtension(parts[^1]));
            }
            if (parts.Length >= 4 && parts[2] == "tree")
            {
                var branch = parts[3];
                var sub = parts.Length > 4 ? string.Join('/', parts[4..]) : null;
                return new ThemeSource(ThemeSourceKind.RemoteZip,
                    $"https://codeload.github.com/{owner}/{repo}/zip/refs/heads/{branch}", name, sub);
            }
            if (parts.Length >= 4 && parts[2] == "archive")
                return new ThemeSource(ThemeSourceKind.RemoteZip, uri.ToString(), name);

            // 기본 브랜치 (GitHub API가 codeload로 리다이렉트)
            return new ThemeSource(ThemeSourceKind.RemoteZip, $"https://api.github.com/repos/{owner}/{repo}/zipball", name);
        }

        var fileName = parts.Length > 0 ? parts[^1] : host;
        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return new ThemeSource(ThemeSourceKind.RemoteZip, uri.ToString(), Path.GetFileNameWithoutExtension(fileName));
        return new ThemeSource(ThemeSourceKind.RemoteFile, uri.ToString(), Path.GetFileNameWithoutExtension(fileName));
    }
}
