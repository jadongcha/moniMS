using System.Text.Json.Serialization;
using MoniMS.Core.Files;

namespace MoniMS.Core.Presets;

/// <summary>
/// 파일 위젯: 바탕화면에 붙은 작은 파일 탐색기 (파일 탐색기의 "자세히" 보기처럼 이름 · 확장자 목록).
/// 왼쪽에는 사용자가 고른 폴더들, 오른쪽에는 연 폴더의 내용이 보인다.
/// 다른 위젯과 달리 클릭을 받는다 (파일을 열고, 끌어 놓고, 우클릭 메뉴를 쓴다).
/// 이동·크기 조절은 편집 모드에서만.
/// </summary>
public sealed class FileWidgetLayout
{
    public const double DefaultWidth = 620;
    public const double DefaultHeight = 400;
    public const double MinWidth = 320;
    public const double MinHeight = 200;
    public const double DefaultExtensionColumnWidth = 96;

    public bool Visible { get; set; } = true;

    /// <summary>왼쪽 목록에 보이는 폴더 (전체 경로, 이 순서대로). 비어 있으면 창을 띄우지 않는다.</summary>
    public List<string> Folders { get; set; } = [];

    /// <summary>마지막으로 연 폴더. 다음에 켤 때 다시 연다. null이면 첫 폴더.</summary>
    public string? CurrentFolder { get; set; }

    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = DefaultWidth;
    public double Height { get; set; } = DefaultHeight;

    public WidgetStyle Style { get; set; } = WidgetStyle.Dark;

    /// <summary>배경만의 불투명도 (0 ~ 1). 글자와 아이콘은 영향 없음.</summary>
    public double Opacity { get; set; } = 0.92;

    public FileSortColumn SortBy { get; set; } = FileSortColumn.Name;
    public bool SortDescending { get; set; }

    /// <summary>확장자 열 너비 (DIP). 이름 열은 나머지를 채운다.</summary>
    public double ExtensionColumnWidth { get; set; } = DefaultExtensionColumnWidth;

    [JsonIgnore]
    public bool HasFolders => Folders.Count > 0;

    public FileWidgetLayout Clone()
    {
        var copy = (FileWidgetLayout)MemberwiseClone();
        copy.Folders = [.. Folders];
        return copy;
    }
}

/// <summary>파일 위젯을 읽고 적용하는 쪽(UI)이 구현.</summary>
public interface IFileWidgetHost
{
    FileWidgetLayout GetCurrentLayout();

    void ApplyLayout(FileWidgetLayout layout);
}
