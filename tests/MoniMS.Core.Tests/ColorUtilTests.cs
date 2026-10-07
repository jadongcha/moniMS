using MoniMS.Core.Desktop;

namespace MoniMS.Core.Tests;

public class ColorUtilTests
{
    [Theory]
    [InlineData("#0078D4", 0x00, 0x78, 0xD4)]
    [InlineData("0078d4", 0x00, 0x78, 0xD4)]
    [InlineData("#FF0078D4", 0x00, 0x78, 0xD4)]
    public void Parse_accepts_common_formats(string hex, byte r, byte g, byte b) =>
        Assert.Equal(new Rgb(r, g, b), Rgb.Parse(hex));

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void Parse_rejects_invalid(string hex) => Assert.Throws<FormatException>(() => Rgb.Parse(hex));

    [Fact]
    public void Abgr_roundtrip_matches_registry_layout()
    {
        var c = Rgb.Parse("#0078D4");
        Assert.Equal(0xFFD47800u, c.ToAbgr()); // 설정 앱이 기록하는 AccentColorMenu 값과 동일
        Assert.Equal(c, Rgb.FromAbgr(c.ToAbgr()));
    }

    [Fact]
    public void Palette_has_8_colors_with_base_in_the_middle_and_ordered_lightness()
    {
        var baseColor = Rgb.Parse("#0078D4");
        var palette = ColorUtil.BuildAccentPalette(baseColor);

        Assert.Equal(8, palette.Length);
        Assert.Equal(baseColor, palette[3]);
        for (var i = 0; i < 6; i++)
            Assert.True(ColorUtil.ToHsl(palette[i]).L >= ColorUtil.ToHsl(palette[i + 1]).L, $"index {i}");
        Assert.Equal(32, ColorUtil.PaletteToBytes(palette).Length);
    }

    [Theory]
    [InlineData("#0078D4")]
    [InlineData("#E81123")]
    [InlineData("#808080")]
    public void Hsl_roundtrip(string hex)
    {
        var c = Rgb.Parse(hex);
        var (h, s, l) = ColorUtil.ToHsl(c);
        var back = ColorUtil.FromHsl(h, s, l);
        Assert.InRange(Math.Abs(back.R - c.R), 0, 1);
        Assert.InRange(Math.Abs(back.G - c.G), 0, 1);
        Assert.InRange(Math.Abs(back.B - c.B), 0, 1);
    }
}
