namespace MoniMS.Core.Presets;

/// <summary>위젯 레이아웃을 읽고 적용하는 쪽(UI)이 구현. Core는 UI 프레임워크를 모른다.</summary>
public interface IWidgetLayoutHost
{
    WidgetLayout GetCurrentLayout();

    void ApplyLayout(WidgetLayout layout);
}
