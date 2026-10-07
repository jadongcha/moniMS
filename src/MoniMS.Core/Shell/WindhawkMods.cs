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

    /// <summary>Windhawk 앱에서 모드 설치 페이지를 여는 링크.</summary>
    public static string InstallLink(string modId) => $"windhawk://mods/{modId}";

    public static string WebLink(string modId) => $"https://windhawk.net/mods/{modId}";
}
