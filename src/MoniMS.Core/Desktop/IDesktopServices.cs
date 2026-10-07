using MoniMS.Core.Presets;

namespace MoniMS.Core.Desktop;

public interface IWallpaperService
{
    /// <summary>현재 배경화면을 읽고, 이미지 파일을 presetDirectory로 복사한다.</summary>
    WallpaperSettings Capture(string presetDirectory);

    void Apply(WallpaperSettings settings, string presetDirectory);

    /// <summary>모든 모니터에 같은 이미지를 배경화면으로.</summary>
    void SetForAllMonitors(string imagePath, WallpaperPosition position);
}

public interface IThemeService
{
    ThemeSettings Capture();

    void Apply(ThemeSettings settings);

    /// <summary>현재 Windows 강조색.</summary>
    Rgb GetAccentColor();
}

public sealed record IconApplyResult(int Moved, int Missing, bool AutoArrangeEnabled);

public interface IDesktopIconService
{
    DesktopIconLayout Capture();

    IconApplyResult Apply(DesktopIconLayout layout);
}
