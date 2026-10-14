namespace MoniMS.Core.Media;

/// <summary>미디어 세션을 낸 앱: 보여 줄 이름과, 그 앱의 소리 세션을 찾을 프로세스 이름들 (확장자 없이).</summary>
public sealed record MediaAppInfo(string Name, IReadOnlyList<string> ProcessNames);

/// <summary>
/// Windows 미디어 제어가 알려 주는 앱 ID(AppUserModelID)로 앱을 알아본다.
/// 예: "Spotify.exe", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"(스토어), "Chrome", "MSEdge",
/// "Chrome._crx_…"(설치한 웹 앱), "308046B0AF4A39CB"(Firefox).
/// </summary>
public static class MediaApps
{
    /// <summary>Chrome·Edge에 설치한 YouTube Music 웹 앱의 확장 ID.</summary>
    private const string YouTubeMusicWebAppId = "cinhimbnkkaeohfgghhklpknlkffjgod";

    /// <summary>앱 ID에 이 글자가 들어 있으면 그 브라우저 (앞에 있을수록 먼저 확인).</summary>
    private static readonly (string Token, string Name, string Process)[] Browsers =
    [
        ("MSEdge", "Edge", "msedge"),
        ("Brave", "Brave", "brave"),
        ("Opera", "Opera", "opera"),
        ("Vivaldi", "Vivaldi", "vivaldi"),
        ("Whale", "Whale", "whale"),
        ("Chrome", "Chrome", "chrome"),
        ("Firefox", "Firefox", "firefox"),
    ];

    /// <summary>Firefox는 앱 ID가 설치 경로의 해시라서 이름으로 알 수 없다 (일반판, 개발자판, 나이틀리).</summary>
    private static readonly HashSet<string> FirefoxIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "308046B0AF4A39CB", "6F193CCC56814779", "E7CF176E110C211B",
    };

    public static MediaAppInfo Describe(string? appId)
    {
        var id = appId?.Trim() ?? "";
        if (id.Length == 0)
            return new MediaAppInfo("Unknown app", []);

        if (id.Contains("spotify", StringComparison.OrdinalIgnoreCase))
            return new MediaAppInfo("Spotify", ["Spotify"]);
        // YouTube Music 데스크톱 앱 (th-ch/youtube-music)
        if (id.Contains("youtube-music", StringComparison.OrdinalIgnoreCase))
            return new MediaAppInfo("YouTube Music", ["YouTube Music"]);
        if (FirefoxIds.Contains(id))
            return new MediaAppInfo("Firefox", ["firefox"]);
        // Windows 미디어 플레이어 (예전 이름 Groove 음악)
        if (id.Contains("ZuneMusic", StringComparison.OrdinalIgnoreCase))
            return new MediaAppInfo("Media Player", ["Microsoft.Media.Player"]);

        foreach (var (token, name, process) in Browsers)
        {
            if (!id.Contains(token, StringComparison.OrdinalIgnoreCase))
                continue;
            // 브라우저에 설치한 웹 앱도 소리는 브라우저 프로세스에서 난다
            return id.Contains(YouTubeMusicWebAppId, StringComparison.OrdinalIgnoreCase)
                ? new MediaAppInfo("YouTube Music", [process])
                : new MediaAppInfo(name, [process]);
        }

        // 일반 프로그램: "Foo.exe" 또는 경로
        if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var exe = Path.GetFileNameWithoutExtension(id);
            return new MediaAppInfo(exe, [exe]);
        }

        // 스토어 앱: "게시자.앱_해시!앱" → 느낌표 뒤 (프로세스 이름도 대개 같다). 그냥 "App"이면 패키지 이름으로.
        var bang = id.LastIndexOf('!');
        if (bang >= 0 && bang < id.Length - 1)
        {
            var app = id[(bang + 1)..];
            return app.Equals("App", StringComparison.OrdinalIgnoreCase)
                ? new MediaAppInfo(PackageName(id[..bang]), [])
                : new MediaAppInfo(app, [app]);
        }
        return new MediaAppInfo(id, []);
    }

    /// <summary>"Contoso.MusicPlayer_8wekyb3d8bbwe" → "MusicPlayer".</summary>
    private static string PackageName(string familyName)
    {
        var name = familyName.Split('_')[0];
        var dot = name.LastIndexOf('.');
        return dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;
    }
}
