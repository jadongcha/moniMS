namespace MoniMS.Core.Shell;

/// <summary>MoniMS가 다루는 Windhawk 모드(작업표시줄·시작 메뉴 커스터마이즈용).</summary>
public static class WindhawkMods
{
    public const string TaskbarStyler = "windows-11-taskbar-styler";
    public const string StartMenuStyler = "windows-11-start-menu-styler";
    public const string NotificationCenterStyler = "windows-11-notification-center-styler";
    public const string ResourceRedirect = "icon-resource-redirect";

    public static IReadOnlyList<string> All { get; } =
        [TaskbarStyler, StartMenuStyler, NotificationCenterStyler, ResourceRedirect];

    public static string DisplayName(string modId) => modId switch
    {
        TaskbarStyler => "Taskbar",
        StartMenuStyler => "Start menu",
        NotificationCenterStyler => "Notification center",
        ResourceRedirect => "Icons (Resource Redirect)",
        _ => modId,
    };

    /// <summary>Windhawk 앱의 Explore 탭에서 검색할 이름.</summary>
    public static string StoreName(string modId) => modId switch
    {
        TaskbarStyler => "Windows 11 Taskbar Styler",
        StartMenuStyler => "Windows 11 Start Menu Styler",
        NotificationCenterStyler => "Windows 11 Notification Center Styler",
        ResourceRedirect => "Resource Redirect",
        _ => modId,
    };

    /// <summary>
    /// Windhawk 앱에서 모드 페이지를 바로 여는 링크. Windhawk 2.0부터만 지원하고,
    /// 1.x에서 열면 VS Code처럼 생긴 Windhawk 편집기 창만 뜨고 아무 일도 안 일어난다.
    /// </summary>
    public static string InstallLink(string modId) => $"windhawk://mods/{modId}";

    public static string WebLink(string modId) => $"https://windhawk.net/mods/{modId}";

    /// <summary>windhawk:// 링크를 쓸 수 있는 버전인지 ("2.0.0-alpha.6" 같은 문자열도 처리).</summary>
    public static bool SupportsDeepLinks(string? version) =>
        version is not null && WindhawkStorage.LeadingInt(version.TrimStart('v', 'V')) >= 2;
}
