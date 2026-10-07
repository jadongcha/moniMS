using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MoniMS.App.ViewModels;
using MoniMS.Core.Desktop;
using MoniMS.Core.Presets;

namespace MoniMS.App.Views;

public partial class WidgetWindow : Window
{
    private readonly WidgetViewModel _vm;

    public WidgetWindow(WidgetViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    /// <summary>사용자가 드래그로 위치를 바꿨을 때.</summary>
    public event EventHandler? MovedByUser;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!_vm.IsEditMode)
            return;
        DragMove();
        MovedByUser?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>null이면 내장 JetBrains Mono.</summary>
    public void ApplyFont(string? family) =>
        FontFamily = string.IsNullOrWhiteSpace(family)
            ? (FontFamily)Application.Current.FindResource("MonoFont")
            : new FontFamily(family);

    /// <param name="backgroundOpacity">
    /// 배경(카드)만의 불투명도 0~1. 글자·그래프는 항상 100%로 그린다.
    /// (창 전체 Opacity를 쓰면 글자까지 흐려지므로 배경 브러시의 알파값만 조절)
    /// </param>
    public void ApplyStyle(WidgetStyle style, Rgb accent, double backgroundOpacity)
    {
        var a = Color.FromRgb(accent.R, accent.G, accent.B);
        // 배경/테두리는 "불투명도 100%일 때"의 색. 유리 스타일은 100%여도 반투명.
        var (bg, border, fg, subtle, track) = style switch
        {
            WidgetStyle.Light => ("#FFF8FAFC", "#1A0F172A", "#0F172A", "#64748B", "#1F0F172A"),
            WidgetStyle.Glass => ("#66000000", "#40FFFFFF", "#FFFFFF", "#E2E8F0", "#33FFFFFF"),
            _ => ("#FF141820", "#22FFFFFF", "#F1F5F9", "#94A3B8", "#22FFFFFF"),
        };
        var opacity = Math.Clamp(backgroundOpacity, 0, 1);

        SetBrush("WidgetBackground", ScaleAlpha(Parse(bg), opacity));
        SetBrush("WidgetBorder", ScaleAlpha(Parse(border), opacity));
        SetBrush("WidgetForeground", Parse(fg));
        SetBrush("WidgetSubtle", Parse(subtle));
        SetBrush("WidgetTrack", Parse(track));
        SetBrush("WidgetAccent", a);
        SetBrush("WidgetAccentFill", Color.FromArgb(0x40, a.R, a.G, a.B));

        // 배경이 많이 비치면(유리 스타일 또는 불투명도 낮음) 글자에 선명한 테두리를 둘러 가독성 확보.
        // 라이트 스타일은 글자가 어두우므로 흰 테두리.
        var seeThrough = style == WidgetStyle.Glass || opacity < 0.6;
        SetBrush("WidgetOutline", style == WidgetStyle.Light ? Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xCC, 0, 0, 0));
        Resources["WidgetOutlineThickness"] = seeThrough ? 1.2 : 0.0;
        Card.Effect = null;
    }

    private static Color ScaleAlpha(Color c, double factor) =>
        Color.FromArgb((byte)Math.Round(c.A * factor), c.R, c.G, c.B);

    private void SetBrush(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Resources[key] = brush;
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
