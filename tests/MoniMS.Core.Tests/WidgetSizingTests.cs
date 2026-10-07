using MoniMS.Core.Presets;

namespace MoniMS.Core.Tests;

public sealed class WidgetSizingTests
{
    [Fact]
    public void Reference_screen_keeps_the_layout_size()
    {
        var (w, h, s) = WidgetSizing.Fit(550, 650, 1, 2880, 1800, 2.0);
        Assert.Equal(550, w, 3);
        Assert.Equal(650, h, 3);
        Assert.Equal(1, s, 3);
    }

    [Theory]
    [InlineData(1920, 1080, 1.0)]
    [InlineData(1920, 1080, 1.25)]
    [InlineData(2560, 1440, 1.5)]
    [InlineData(3840, 2160, 1.5)]
    [InlineData(1366, 768, 1.0)]
    public void Same_screen_fraction_on_every_resolution(double sw, double sh, double dpi)
    {
        var (w, h, s) = WidgetSizing.Fit(550, 650, 1, sw, sh, dpi);

        // 물리 픽셀 기준으로 화면에서 차지하는 비율이 기준 화면과 같다
        Assert.Equal(550 * 2.0 / 2880, w * dpi / sw, 4); // 가로 ≈ 38.2%
        Assert.Equal(650 * 2.0 / 1800, h * dpi / sh, 4); // 세로 ≈ 72.2%

        // 내용은 찌그러지지 않게 균일 배율, 창 안에 들어감
        Assert.True(550 * s <= w + 0.001 && 650 * s <= h + 0.001);
        Assert.True(Math.Abs(550 * s - w) < 0.001 || Math.Abs(650 * s - h) < 0.001);
    }

    [Fact]
    public void Full_hd_at_100_percent_example()
    {
        var (w, h, s) = WidgetSizing.Fit(550, 650, 1, 1920, 1080, 1.0);
        Assert.Equal(733.3, w, 1);
        Assert.Equal(780, h, 1);
        Assert.Equal(1.2, s, 3);
    }

    [Fact]
    public void User_scale_multiplies_and_bad_input_falls_back()
    {
        var (w, h, s) = WidgetSizing.Fit(550, 650, 0.5, 2880, 1800, 2.0);
        Assert.Equal((275.0, 325.0, 0.5), (Math.Round(w, 3), Math.Round(h, 3), Math.Round(s, 3)));
        Assert.Equal((550.0, 650.0, 1.0), WidgetSizing.Fit(550, 650, 1, 0, 0, 1));
    }

}
