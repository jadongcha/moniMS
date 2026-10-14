namespace MoniMS.Core.Presets;

/// <summary>
/// 음악 위젯: 지금 재생 중인 곡(Spotify, 브라우저의 YouTube·YouTube Music 등 Windows 미디어 제어에 나오는 앱)을 보여 주고
/// 재생/일시 정지, 이전·다음 곡, 음량을 조절한다. 버튼을 눌러야 하므로 클릭 통과가 아니다.
/// 이동·크기 조절은 편집 모드에서만.
/// </summary>
public sealed class MediaWidgetLayout
{
    public const double DefaultWidth = 360;
    public const double DefaultHeight = 160;
    public const double MinWidth = 260;
    public const double MinHeight = 130;

    /// <summary>처음에는 숨김. 설정 창이나 트레이 메뉴에서 켠다.</summary>
    public bool Visible { get; set; }

    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = DefaultWidth;
    public double Height { get; set; } = DefaultHeight;

    public WidgetStyle Style { get; set; } = WidgetStyle.Dark;

    /// <summary>배경만의 불투명도 (0 ~ 1). 글자와 버튼은 영향 없음.</summary>
    public double Opacity { get; set; } = 0.92;

    /// <summary>재생 버튼 · 진행 막대 · 음량 막대 색 (#RRGGBB). null이면 Windows 강조색.</summary>
    public string? AccentColor { get; set; }

    /// <summary>음량 막대가 조절하는 대상.</summary>
    public MediaVolumeTarget VolumeTarget { get; set; } = MediaVolumeTarget.App;

    public MediaWidgetLayout Clone() => (MediaWidgetLayout)MemberwiseClone();
}

public enum MediaVolumeTarget
{
    /// <summary>재생 중인 앱의 음량 (볼륨 믹서의 그 앱 항목). 다른 소리는 그대로.</summary>
    App,

    /// <summary>Windows 전체 음량.</summary>
    System,
}

/// <summary>음악 위젯을 읽고 적용하는 쪽(UI)이 구현.</summary>
public interface IMediaWidgetHost
{
    MediaWidgetLayout GetCurrentLayout();

    void ApplyLayout(MediaWidgetLayout layout);
}
