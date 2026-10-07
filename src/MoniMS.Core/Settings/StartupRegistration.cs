using Microsoft.Win32;

namespace MoniMS.Core.Settings;

/// <summary>HKCU\...\Run 에 등록해 Windows 로그인 시 자동 실행.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MoniMS";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{executablePath}\" --background");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
