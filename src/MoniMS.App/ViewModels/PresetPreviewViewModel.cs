using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MoniMS.Core.Desktop;
using MoniMS.Core.Presets;

namespace MoniMS.App.ViewModels;

public sealed record PreviewIcon(double X, double Y, string Name);

public sealed record PreviewRow(string Label, string Value);

/// <summary>
/// 선택한 프리셋의 미리보기: 축소된 바탕화면(배경화면 + 아이콘 위치 + 위젯 위치)과 저장된 항목 요약.
/// 미리보기 캔버스 좌표 = 주 모니터의 실제 픽셀 좌표 (Viewbox가 화면 크기에 맞게 줄인다).
/// </summary>
public sealed class PresetPreviewViewModel
{
    public const double IconSize = 56;

    private PresetPreviewViewModel(string name) => Name = name;

    public string Name { get; }
    public double CanvasWidth { get; private init; } = 1920;
    public double CanvasHeight { get; private init; } = 1080;
    public ImageSource? WallpaperImage { get; private init; }
    public Stretch WallpaperStretch { get; private init; } = Stretch.UniformToFill;
    public Brush DesktopBackground { get; private init; } = Brushes.Black;
    public IReadOnlyList<PreviewIcon> Icons { get; private init; } = [];
    public bool HasWidget { get; private init; }
    public double WidgetLeft { get; private init; }
    public double WidgetTop { get; private init; }
    public double WidgetWidth { get; private init; }
    public double WidgetHeight { get; private init; }
    public Brush WidgetBrush { get; private init; } = Brushes.Transparent;
    public Brush? AccentSwatch { get; private init; }
    public string AccentText { get; private init; } = "";
    public IReadOnlyList<PreviewRow> Rows { get; private init; } = [];
    public string? MissingNote { get; private init; }

    public static PresetPreviewViewModel Create(Preset preset, string presetDirectory)
    {
        // 기준 화면 크기(픽셀): 아이콘 저장 당시 해상도 → 주 모니터 배경화면 정보 → 현재 화면
        var primary = preset.Wallpaper?.Monitors
            .OrderBy(m => m.Left == 0 && m.Top == 0 ? 0 : 1).ThenBy(m => m.Index).FirstOrDefault();
        double width = preset.Icons is { ScreenWidth: > 0 } i ? i.ScreenWidth
            : primary is { Width: > 0 } ? primary.Width
            : SystemParameters.PrimaryScreenWidth;
        double height = preset.Icons is { ScreenHeight: > 0 } j ? j.ScreenHeight
            : primary is { Height: > 0 } ? primary.Height
            : SystemParameters.PrimaryScreenHeight;

        // 위젯 좌표는 DIP 단위라서 픽셀로 바꿀 비율 (현재 화면 DPI 기준 근사치)
        var dpiScale = width / SystemParameters.PrimaryScreenWidth;

        var rows = new List<PreviewRow>();

        // ----- 배경화면 -----
        ImageSource? image = null;
        var stretch = Stretch.UniformToFill;
        Brush background = Brushes.Black;
        if (preset.Wallpaper is { } wp)
        {
            background = SolidFromHex(wp.BackgroundColor) ?? Brushes.Black;
            var file = primary?.ImageFile ?? wp.Monitors.FirstOrDefault(m => m.ImageFile is not null)?.ImageFile;
            if (file is not null)
                image = LoadThumbnail(Path.Combine(presetDirectory, file));
            stretch = wp.Position switch
            {
                WallpaperPosition.Fit => Stretch.Uniform,
                WallpaperPosition.Stretch => Stretch.Fill,
                WallpaperPosition.Center or WallpaperPosition.Tile => Stretch.None,
                _ => Stretch.UniformToFill,
            };
            rows.Add(new PreviewRow("Wallpaper",
                $"{wp.Monitors.Count} monitor(s) · {wp.Position}" + (image is null && file is not null ? " · image missing" : "")));
        }
        else
        {
            rows.Add(new PreviewRow("Wallpaper", "not saved"));
        }

        // ----- 테마 -----
        Brush? swatch = null;
        var accentText = "";
        if (preset.Theme is { } th)
        {
            swatch = th.AutoAccentFromWallpaper ? null : SolidFromHex(th.AccentColor);
            accentText = th.AutoAccentFromWallpaper ? "auto (from wallpaper)" : th.AccentColor.ToUpperInvariant();
            rows.Add(new PreviewRow("Theme",
                $"apps {(th.AppsUseLightTheme ? "light" : "dark")} · system {(th.SystemUsesLightTheme ? "light" : "dark")}" +
                (th.EnableTransparency ? " · transparency" : "")));
        }
        else
        {
            rows.Add(new PreviewRow("Theme", "not saved"));
        }

        // ----- 아이콘 -----
        var icons = new List<PreviewIcon>();
        if (preset.Icons is { } il)
        {
            icons.AddRange(il.Icons.Select(x => new PreviewIcon(x.X, x.Y, x.Name)));
            rows.Add(new PreviewRow("Icons", $"{il.Icons.Count} positions"));
        }
        else
        {
            rows.Add(new PreviewRow("Icons", "not saved"));
        }

        // ----- 위젯 -----
        var hasWidget = false;
        double wl = 0, wt = 0, ww = 0, wh = 0;
        Brush widgetBrush = Brushes.Transparent;
        if (preset.Widget is { } w)
        {
            hasWidget = w.Visible;
            // 실제 위젯과 같은 규칙: 화면에서 차지하는 비율이 해상도와 관계없이 같음
            var sized = w.Clone();
            sized.UpgradeLegacySize(); // 적용할 때와 같은 크기로 미리보기
            var (fw, fh, _) = WidgetSizing.Fit(sized.Width, sized.Height, sized.Scale, width, height, dpiScale);
            ww = fw * dpiScale;
            wh = fh * dpiScale;
            if (double.IsNaN(w.Left) || double.IsNaN(w.Top))
            {
                wl = width - ww - 24 * dpiScale; // 기본 위치: 오른쪽 위
                wt = 24 * dpiScale;
            }
            else
            {
                wl = w.Left * dpiScale;
                wt = w.Top * dpiScale;
            }
            var (baseColor, baseAlpha) = w.Style switch
            {
                WidgetStyle.Light => (Color.FromRgb(0xF8, 0xFA, 0xFC), 1.0),
                WidgetStyle.Glass => (Color.FromRgb(0, 0, 0), 0.4),
                _ => (Color.FromRgb(0x14, 0x18, 0x20), 1.0),
            };
            // 투명해도 위치는 보이게 최소 알파를 둔다
            var alpha = Math.Max(0.25, baseAlpha * w.Opacity);
            widgetBrush = Frozen(new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), baseColor.R, baseColor.G, baseColor.B)));

            var font = string.IsNullOrWhiteSpace(w.FontFamily) ? "JetBrains Mono" : w.FontFamily;
            rows.Add(new PreviewRow("Widget", w.Visible
                ? $"{w.Style} · {w.Width:0}×{w.Height:0} · bg {w.Opacity.ToString("P0", CultureInfo.InvariantCulture)} · {font}"
                : "hidden"));
            rows.Add(new PreviewRow("Sections", string.Join(", ", w.Sections)));
        }
        else
        {
            rows.Add(new PreviewRow("Widget", "not saved"));
        }

        rows.Add(new PreviewRow("Saved", preset.UpdatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));

        return new PresetPreviewViewModel(preset.Name)
        {
            CanvasWidth = width,
            CanvasHeight = height,
            WallpaperImage = image,
            WallpaperStretch = stretch,
            DesktopBackground = background,
            Icons = icons,
            HasWidget = hasWidget,
            WidgetLeft = wl,
            WidgetTop = wt,
            WidgetWidth = ww,
            WidgetHeight = wh,
            WidgetBrush = widgetBrush,
            AccentSwatch = swatch,
            AccentText = accentText,
            Rows = rows,
        };
    }

    /// <summary>파일을 잠그지 않도록 메모리로 읽고, 미리보기 크기로 줄여서 디코딩.</summary>
    private static BitmapImage? LoadThumbnail(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 640;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null; // 손상된 이미지
        }
    }

    private static SolidColorBrush? SolidFromHex(string? hex)
    {
        try
        {
            var c = Rgb.Parse(hex ?? "");
            return Frozen(new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static SolidColorBrush Frozen(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
