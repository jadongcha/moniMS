using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MoniMS.App.Controls;

/// <summary>
/// 글자 테두리(외곽선)를 그릴 수 있는 텍스트. WPF TextBlock은 외곽선을 지원하지 않아서
/// 글자를 도형(Geometry)으로 바꾼 뒤 테두리 → 채우기 순서로 직접 그린다.
/// 그림자(번짐)와 달리 가장자리가 선명하다.
/// </summary>
public sealed class OutlinedText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(OutlinedText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    // 글꼴/색은 TextBlock처럼 부모(창)에서 상속받는다
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(OutlinedText), new FrameworkPropertyMetadata(Brushes.Black,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(OutlinedText), new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, InheritsMeasure));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(OutlinedText), new FrameworkPropertyMetadata(SystemFonts.MessageFontSize, InheritsMeasure));

    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(
        typeof(OutlinedText), new FrameworkPropertyMetadata(FontWeights.Normal, InheritsMeasure));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(OutlinedText),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>글자 바깥쪽 테두리 두께(px). 0이면 테두리 없음.</summary>
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(OutlinedText),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextTrimmingProperty = DependencyProperty.Register(
        nameof(TextTrimming), typeof(TextTrimming), typeof(OutlinedText),
        new FrameworkPropertyMetadata(TextTrimming.None, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private const FrameworkPropertyMetadataOptions InheritsMeasure =
        FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender;

    private FormattedText? _formatted;

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public TextTrimming TextTrimming { get => (TextTrimming)GetValue(TextTrimmingProperty); set => SetValue(TextTrimmingProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Text ?? "";
        _formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal),
            FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            Trimming = TextTrimming,
        };
        if (!double.IsInfinity(availableSize.Width) && availableSize.Width > 0)
            _formatted.MaxTextWidth = availableSize.Width;
        if (TextTrimming != TextTrimming.None && !text.Contains('\n'))
            _formatted.MaxLineCount = 1;

        return new Size(Math.Min(_formatted.WidthIncludingTrailingWhitespace, availableSize.Width), _formatted.Height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_formatted is null || string.IsNullOrEmpty(Text))
            return;

        _formatted.SetForegroundBrush(Foreground);
        if (Stroke is null || StrokeThickness <= 0)
        {
            drawingContext.DrawText(_formatted, new Point(0, 0)); // 테두리 없으면 일반 텍스트 렌더링(가장 선명)
            return;
        }

        var geometry = _formatted.BuildGeometry(new Point(0, 0));
        // 펜은 경로 중심선 기준이라 두께를 2배로 그리고 그 위에 글자를 채우면 바깥쪽 테두리만 남는다
        var pen = new Pen(Stroke, StrokeThickness * 2) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
        drawingContext.DrawGeometry(Foreground, null, geometry);
    }
}
