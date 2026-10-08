using Microsoft.Win32;
using MoniMS.Core.Interop;
using MoniMS.Core.Presets;

namespace MoniMS.Core.Desktop;

/// <summary>
/// 다크/라이트 모드, 투명 효과, 강조색. 설정 앱이 쓰는 것과 같은 HKCU 레지스트리 값을 쓰고
/// WM_SETTINGCHANGE("ImmersiveColorSet")를 브로드캐스트해 즉시 반영한다. 관리자 권한 불필요.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";
    private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string DesktopKey = @"Control Panel\Desktop";

    public ThemeSettings Capture()
    {
        using var personalize = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        using var dwm = Registry.CurrentUser.OpenSubKey(DwmKey);
        using var desktop = Registry.CurrentUser.OpenSubKey(DesktopKey);

        return new ThemeSettings
        {
            AppsUseLightTheme = ReadBool(personalize, "AppsUseLightTheme", true),
            SystemUsesLightTheme = ReadBool(personalize, "SystemUsesLightTheme", false),
            EnableTransparency = ReadBool(personalize, "EnableTransparency", true),
            AccentOnStartAndTaskbar = ReadBool(personalize, "ColorPrevalence", false),
            AccentOnTitleBars = ReadBool(dwm, "ColorPrevalence", false),
            AutoAccentFromWallpaper = ReadBool(desktop, "AutoColorization", false),
            AccentColor = GetAccentColor().ToString(),
        };
    }

    public void Apply(ThemeSettings settings)
    {
        // 이미 같은 상태면 아무것도 하지 않는다. 변경 알림은 모든 창이 응답할 때까지 기다려서 수백 ms~수 초가 걸린다.
        if (IsCurrent(Capture(), settings))
            return;

        using (var personalize = Registry.CurrentUser.CreateSubKey(PersonalizeKey))
        {
            WriteBool(personalize, "AppsUseLightTheme", settings.AppsUseLightTheme);
            WriteBool(personalize, "SystemUsesLightTheme", settings.SystemUsesLightTheme);
            WriteBool(personalize, "EnableTransparency", settings.EnableTransparency);
            // 작업표시줄 강조색은 시스템 다크 모드에서만 허용됨 (설정 앱과 동일 규칙)
            WriteBool(personalize, "ColorPrevalence", settings.AccentOnStartAndTaskbar && !settings.SystemUsesLightTheme);
        }

        using (var desktop = Registry.CurrentUser.CreateSubKey(DesktopKey))
            WriteBool(desktop, "AutoColorization", settings.AutoAccentFromWallpaper);

        var accent = Rgb.Parse(settings.AccentColor);
        var palette = ColorUtil.BuildAccentPalette(accent);

        using (var dwm = Registry.CurrentUser.CreateSubKey(DwmKey))
        {
            WriteBool(dwm, "ColorPrevalence", settings.AccentOnTitleBars);
            if (!settings.AutoAccentFromWallpaper)
            {
                dwm.SetValue("AccentColor", unchecked((int)accent.ToAbgr()), RegistryValueKind.DWord);
                // ColorizationColor는 ARGB 순서
                var argb = 0xC4u << 24 | (uint)accent.R << 16 | (uint)accent.G << 8 | accent.B;
                dwm.SetValue("ColorizationColor", unchecked((int)argb), RegistryValueKind.DWord);
                dwm.SetValue("ColorizationAfterglow", unchecked((int)argb), RegistryValueKind.DWord);
            }
        }

        if (!settings.AutoAccentFromWallpaper)
        {
            using var accentKey = Registry.CurrentUser.CreateSubKey(AccentKey);
            accentKey.SetValue("AccentPalette", ColorUtil.PaletteToBytes(palette), RegistryValueKind.Binary);
            accentKey.SetValue("AccentColorMenu", unchecked((int)palette[3].ToAbgr()), RegistryValueKind.DWord);
            accentKey.SetValue("StartColorMenu", unchecked((int)palette[4].ToAbgr()), RegistryValueKind.DWord);
        }

        Broadcast();
    }

    public Rgb GetAccentColor()
    {
        using var accent = Registry.CurrentUser.OpenSubKey(AccentKey);
        if (accent?.GetValue("AccentColorMenu") is int v)
            return Rgb.FromAbgr(unchecked((uint)v));
        using var dwm = Registry.CurrentUser.OpenSubKey(DwmKey);
        if (dwm?.GetValue("AccentColor") is int d)
            return Rgb.FromAbgr(unchecked((uint)d));
        return Rgb.Parse("#0078D4");
    }

    /// <summary>지금 상태(current)에 target을 적용해도 바뀌는 것이 없는지.</summary>
    internal static bool IsCurrent(ThemeSettings current, ThemeSettings target)
    {
        if (current.AppsUseLightTheme != target.AppsUseLightTheme
            || current.SystemUsesLightTheme != target.SystemUsesLightTheme
            || current.EnableTransparency != target.EnableTransparency
            || current.AccentOnStartAndTaskbar != (target.AccentOnStartAndTaskbar && !target.SystemUsesLightTheme)
            || current.AccentOnTitleBars != target.AccentOnTitleBars
            || current.AutoAccentFromWallpaper != target.AutoAccentFromWallpaper)
            return false;
        if (target.AutoAccentFromWallpaper)
            return true; // 강조색은 배경화면이 정한다
        try
        {
            return Rgb.Parse(current.AccentColor) == Rgb.Parse(target.AccentColor);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void Broadcast()
    {
        NativeMethods.SendMessageTimeout(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE, IntPtr.Zero,
            "ImmersiveColorSet", NativeMethods.SMTO_ABORTIFHUNG, 1000, out _);
        NativeMethods.SendMessageTimeout(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE, IntPtr.Zero,
            "WindowsThemeElement", NativeMethods.SMTO_ABORTIFHUNG, 1000, out _);
        NativeMethods.SendMessageTimeout(NativeMethods.HWND_BROADCAST, NativeMethods.WM_DWMCOLORIZATIONCOLORCHANGED,
            IntPtr.Zero, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, 1000, out _);
        NativeMethods.SendMessageTimeout(NativeMethods.HWND_BROADCAST, NativeMethods.WM_THEMECHANGED,
            IntPtr.Zero, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, 1000, out _);
    }

    private static bool ReadBool(RegistryKey? key, string name, bool fallback) =>
        key?.GetValue(name) is int v ? v != 0 : fallback;

    private static void WriteBool(RegistryKey key, string name, bool value) =>
        key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
}
