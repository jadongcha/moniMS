namespace MoniMS.Core.Presets;

/// <summary>
/// 위젯이 어느 해상도에서든 화면에서 같은 "비율"을 차지하도록 크기를 계산한다.
/// 기준: 2880×1800 화면(배율 200%)에서 레이아웃 크기(기본 478×695)로 보이는 모습.
/// 예) 478×695 → 기준 화면에서 가로 33.2%, 세로 77.2% → 1920×1080 화면에서도 같은 비율.
/// </summary>
public static class WidgetSizing
{
    public const double ReferenceScreenWidth = 2880;
    public const double ReferenceScreenHeight = 1800;
    public const double ReferenceDpiScale = 2.0;

    /// <param name="designWidth">레이아웃 너비 (기준 화면에서의 DIP)</param>
    /// <param name="designHeight">레이아웃 높이 (기준 화면에서의 DIP)</param>
    /// <param name="userScale">사용자 배율 (기본 1)</param>
    /// <param name="screenWidth">위젯이 있는 모니터 너비 (물리 픽셀)</param>
    /// <param name="screenHeight">위젯이 있는 모니터 높이 (물리 픽셀)</param>
    /// <param name="dpiScale">그 모니터의 Windows 배율 (1.0 = 100%)</param>
    /// <returns>창 크기(DIP)와 내용 배율. 화면 비율이 기준과 다르면 내용은 짧은 쪽에 맞춰 균일하게 키우고, 남는 쪽은 내용이 넓게 펼쳐진다.</returns>
    public static (double WindowWidth, double WindowHeight, double ContentScale) Fit(
        double designWidth, double designHeight, double userScale,
        double screenWidth, double screenHeight, double dpiScale)
    {
        if (screenWidth <= 0 || screenHeight <= 0 || dpiScale <= 0)
            return (designWidth * userScale, designHeight * userScale, userScale);

        // 기준 화면에서 차지하는 비율
        var fractionX = designWidth * ReferenceDpiScale / ReferenceScreenWidth;
        var fractionY = designHeight * ReferenceDpiScale / ReferenceScreenHeight;

        // 이 화면에서 같은 비율이 되는 크기 (물리 픽셀 → DIP)
        var width = fractionX * screenWidth * userScale / dpiScale;
        var height = fractionY * screenHeight * userScale / dpiScale;

        var scale = Math.Clamp(Math.Min(width / designWidth, height / designHeight), 0.3, 4.0);
        return (width, height, scale);
    }
}
