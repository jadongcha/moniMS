using System.Text.Json.Serialization;

namespace MoniMS.Core.Presets;

/// <summary>
/// 사진/GIF 위젯. 정보 위젯처럼 바탕화면에 붙어 있고 항상 클릭 통과이며,
/// 편집 모드에서만 끌어서 옮기고 가장자리를 끌어 크기를 바꾼다 (사진 비율 유지).
/// </summary>
public sealed class ImageWidgetLayout
{
    public const double DefaultWidth = 320;
    public const double MinSize = 48;

    public bool Visible { get; set; } = true;

    /// <summary>
    /// 보여줄 이미지. 설정(AppSettings)에서는 전체 경로(%APPDATA%\MoniMS\images\...),
    /// 프리셋에서는 프리셋 폴더 안의 파일 이름(복사본). null이면 이미지 없음 → 창을 띄우지 않는다.
    /// </summary>
    public string? ImagePath { get; set; }

    /// <summary>사용자가 고른 원래 파일 이름 (표시용).</summary>
    public string? ImageName { get; set; }

    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;

    /// <summary>창 크기 (DIP). 이미지는 이 크기에 맞춰 늘어나고 줄어든다.</summary>
    public double Width { get; set; } = DefaultWidth;
    public double Height { get; set; } = DefaultWidth;

    /// <summary>이미지 불투명도 (0.1 ~ 1).</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>모서리 둥글기 (DIP).</summary>
    public double CornerRadius { get; set; } = 12;

    [JsonIgnore]
    public bool HasImage => !string.IsNullOrEmpty(ImagePath);

    public ImageWidgetLayout Clone() => (ImageWidgetLayout)MemberwiseClone();
}

/// <summary>사진 위젯을 읽고 적용하는 쪽(UI)이 구현. ImagePath는 항상 전체 경로로 주고받는다.</summary>
public interface IImageWidgetHost
{
    ImageWidgetLayout GetCurrentLayout();

    void ApplyLayout(ImageWidgetLayout layout);
}
