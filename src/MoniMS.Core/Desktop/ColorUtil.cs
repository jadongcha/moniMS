using System.Globalization;

namespace MoniMS.Core.Desktop;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Parse(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length == 8)
            s = s[2..]; // AARRGGBB → RRGGBB
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"Invalid color: {hex}");
        return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>레지스트리 DWORD에 쓰이는 0xAABBGGRR 형식.</summary>
    public uint ToAbgr(byte alpha = 0xFF) => (uint)(alpha << 24 | B << 16 | G << 8 | R);

    public static Rgb FromAbgr(uint v) => new((byte)v, (byte)(v >> 8), (byte)(v >> 16));

    /// <summary>COLORREF (0x00BBGGRR)</summary>
    public uint ToColorRef() => ToAbgr(0);
}

public static class ColorUtil
{
    /// <summary>
    /// Windows 강조색 팔레트(AccentPalette, 8색 x RGBA = 32바이트) 생성.
    /// 순서: Light3, Light2, Light1, Base, Dark1, Dark2, Dark3, (보조색).
    /// </summary>
    public static Rgb[] BuildAccentPalette(Rgb baseColor)
    {
        var (h, s, l) = ToHsl(baseColor);
        Rgb Shift(double dl) => FromHsl(h, s, Math.Clamp(l + dl, 0.04, 0.96));
        return
        [
            Shift(+0.30), Shift(+0.20), Shift(+0.10),
            baseColor,
            Shift(-0.08), Shift(-0.18), Shift(-0.28),
            new Rgb(0x88, 0x88, 0x88),
        ];
    }

    /// <summary>색 고르기 표의 색상(도): 빨강 → 주황 → 노랑 → 초록 → 청록 → 파랑 → 보라 → 분홍.</summary>
    private static readonly int[] PickerHues = [0, 25, 45, 60, 90, 140, 170, 195, 215, 240, 275, 315];

    /// <summary>색 고르기 표의 밝기: 위(밝음) → 아래(어두움).</summary>
    private static readonly double[] PickerLightness = [0.85, 0.72, 0.60, 0.50, 0.40, 0.28];

    /// <summary>색 고르기 표의 열 수.</summary>
    public static int PickerColumns => PickerHues.Length;

    /// <summary>
    /// 색 고르기 표 (행 순서). 가로는 색상, 세로는 밝기이고 맨 아래 줄은 흰색 → 검은색 회색 단계.
    /// </summary>
    public static IReadOnlyList<Rgb> BuildPickerPalette()
    {
        var list = new List<Rgb>(PickerHues.Length * (PickerLightness.Length + 1));
        foreach (var l in PickerLightness)
        {
            foreach (var h in PickerHues)
                list.Add(FromHsl(h / 360.0, 0.85, l));
        }
        for (var i = 0; i < PickerHues.Length; i++)
            list.Add(FromHsl(0, 0, 1.0 - (double)i / (PickerHues.Length - 1)));
        return list;
    }

    public static byte[] PaletteToBytes(IReadOnlyList<Rgb> palette)
    {
        var bytes = new byte[palette.Count * 4];
        for (var i = 0; i < palette.Count; i++)
        {
            bytes[i * 4] = palette[i].R;
            bytes[i * 4 + 1] = palette[i].G;
            bytes[i * 4 + 2] = palette[i].B;
            bytes[i * 4 + 3] = 0;
        }
        return bytes;
    }

    internal static (double H, double S, double L) ToHsl(Rgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double h = 0, s = 0, l = (max + min) / 2;
        if (max != min)
        {
            var d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
        }
        return (h, s, l);
    }

    internal static Rgb FromHsl(double h, double s, double l)
    {
        if (s == 0)
        {
            var v = (byte)Math.Round(l * 255);
            return new Rgb(v, v, v);
        }
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        var q2 = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p2 = 2 * l - q2;
        return new Rgb(
            (byte)Math.Round(Hue(p2, q2, h + 1.0 / 3) * 255),
            (byte)Math.Round(Hue(p2, q2, h) * 255),
            (byte)Math.Round(Hue(p2, q2, h - 1.0 / 3) * 255));
    }
}
