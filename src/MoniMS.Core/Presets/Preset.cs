using System.Text.Json.Serialization;

namespace MoniMS.Core.Presets;

/// <summary>프리셋 하나 = 바탕화면 상태 스냅샷. 각 항목은 null이면 "저장 안 함"을 의미.</summary>
public sealed class Preset
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New preset";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public WallpaperSettings? Wallpaper { get; set; }
    public ThemeSettings? Theme { get; set; }
    public DesktopIconLayout? Icons { get; set; }
    public WidgetLayout? Widget { get; set; }

    [JsonIgnore]
    public PresetParts AvailableParts =>
        (Wallpaper is null ? 0 : PresetParts.Wallpaper)
        | (Theme is null ? 0 : PresetParts.Theme)
        | (Icons is null ? 0 : PresetParts.Icons)
        | (Widget is null ? 0 : PresetParts.Widget);
}

[Flags]
public enum PresetParts
{
    None = 0,
    Wallpaper = 1,
    Theme = 2,
    Icons = 4,
    Widget = 8,
    All = Wallpaper | Theme | Icons | Widget,
}

// ---------- 배경화면 ----------
public enum WallpaperPosition
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5,
}

public sealed class WallpaperSettings
{
    public WallpaperPosition Position { get; set; } = WallpaperPosition.Fill;

    /// <summary>이미지가 없을 때 보이는 단색 배경 (#RRGGBB).</summary>
    public string BackgroundColor { get; set; } = "#000000";

    public List<MonitorWallpaper> Monitors { get; set; } = [];
}

public sealed class MonitorWallpaper
{
    /// <summary>IDesktopWallpaper의 모니터 디바이스 경로. 모니터 연결이 바뀌면 달라질 수 있어서 Index로도 매칭.</summary>
    public string MonitorId { get; set; } = "";
    public int Index { get; set; }
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>프리셋 폴더 안의 파일명(복사본). 원본이 지워져도 프리셋이 유지되도록 복사해 둔다.</summary>
    public string? ImageFile { get; set; }
}

// ---------- 테마 ----------
public sealed class ThemeSettings
{
    public bool AppsUseLightTheme { get; set; }
    public bool SystemUsesLightTheme { get; set; }
    public bool EnableTransparency { get; set; } = true;

    /// <summary>시작 메뉴/작업표시줄에 강조색 표시 (시스템 다크 모드일 때만 적용됨).</summary>
    public bool AccentOnStartAndTaskbar { get; set; }

    public bool AccentOnTitleBars { get; set; }

    /// <summary>배경화면에서 자동으로 강조색 선택.</summary>
    public bool AutoAccentFromWallpaper { get; set; }

    /// <summary>#RRGGBB</summary>
    public string AccentColor { get; set; } = "#0078D4";
}

// ---------- 아이콘 배치 ----------
public sealed class DesktopIconLayout
{
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public List<IconPosition> Icons { get; set; } = [];
}

public sealed class IconPosition
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
}

// ---------- 정보 위젯 ----------
public enum WidgetSection
{
    System,
    Cpu,
    Memory,
    Gpu,
    Disk,
    Network,
    Uptime,
}

public enum WidgetStyle
{
    Dark,
    Light,
    Glass,
}

/// <remarks>위젯은 항상 클릭 통과(마우스가 바탕화면으로 감). 위치 편집 모드에서만 잡을 수 있다.</remarks>
public sealed class WidgetLayout
{
    public bool Visible { get; set; } = true;
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 550;
    public double Height { get; set; } = 650;
    /// <summary>배경만의 불투명도 (0 = 완전 투명, 1 = 불투명). 글자는 영향 없음.</summary>
    public double Opacity { get; set; } = 0.92;
    public double Scale { get; set; } = 1.0;
    public WidgetStyle Style { get; set; } = WidgetStyle.Dark;

    /// <summary>글꼴 이름. null이면 앱 내장 JetBrains Mono.</summary>
    public string? FontFamily { get; set; }

    /// <summary>게이지 색 (#RRGGBB). 비어 있으면 Windows 강조색 사용.</summary>
    public string? AccentColor { get; set; }

    /// <summary>표시 순서 = 리스트 순서.</summary>
    public List<WidgetSection> Sections { get; set; } =
    [
        WidgetSection.System, WidgetSection.Cpu, WidgetSection.Memory, WidgetSection.Gpu,
        WidgetSection.Disk, WidgetSection.Network, WidgetSection.Uptime,
    ];

    public WidgetLayout Clone()
    {
        var copy = (WidgetLayout)MemberwiseClone();
        copy.Sections = [.. Sections];
        return copy;
    }
}
