using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MoniMS.Core.Settings;

namespace MoniMS.App.Services;

/// <summary>
/// 설정 창(및 대화상자)의 라이트/다크 테마 전환.
/// App.xaml의 첫 번째 병합 사전(색상 팔레트)을 교체하면 DynamicResource로 묶인 모든 색이 즉시 바뀐다.
/// 위젯은 자체 스타일(Dark/Light/Glass)을 쓰므로 영향을 받지 않는다.
/// </summary>
public static partial class UiThemeManager
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static UiTheme Current { get; private set; } = UiTheme.Light;

    public static void Apply(UiTheme theme)
    {
        Current = theme;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary
        {
            Source = new Uri($"/MoniMS;component/Themes/{theme}.xaml", UriKind.Relative),
        };
        if (dictionaries.Count > 0)
            dictionaries[0] = palette;
        else
            dictionaries.Insert(0, palette);

        foreach (Window window in Application.Current.Windows)
            ApplyTitleBar(window);
    }

    /// <summary>Windows 11 제목 표시줄도 다크로 (창 핸들이 생긴 뒤 호출).</summary>
    public static void ApplyTitleBar(Window window)
    {
        if (window is Views.WidgetWindow)
            return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var dark = Current == UiTheme.Dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    /// <summary>창이 만들어질 때 제목 표시줄 테마를 맞추도록 연결.</summary>
    public static void Attach(Window window) =>
        window.SourceInitialized += (_, _) => ApplyTitleBar(window);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
