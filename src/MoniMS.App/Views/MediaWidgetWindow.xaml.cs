using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MoniMS.App.ViewModels;
using MoniMS.Core.Presets;

namespace MoniMS.App.Views;

/// <summary>
/// 음악 위젯 창: 앨범 아트 · 곡 정보 · 진행 막대 · 이전/재생/다음 · 음량.
/// 버튼은 언제나 누를 수 있다 (클릭 통과 아님, 키보드는 받지 않음). 위에서 휠을 굴리면 음량.
/// 편집 모드에서는 안을 끌면 이동, 가장자리를 끌면 크기 조절 (파일 위젯과 같은 방식).
/// </summary>
public partial class MediaWidgetWindow : Window
{
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_CAPTURECHANGED = 0x0215;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    private const int WMSZ_LEFT = 1, WMSZ_RIGHT = 2, WMSZ_TOP = 3, WMSZ_TOPLEFT = 4, WMSZ_TOPRIGHT = 5, WMSZ_BOTTOM = 6, WMSZ_BOTTOMLEFT = 7, WMSZ_BOTTOMRIGHT = 8;
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    /// <summary>크기 조절을 잡는 가장자리 폭 / 오른쪽 아래 모서리 크기 (DIP).</summary>
    private const double EdgeSize = 10;
    private const double CornerSize = 22;

    private readonly MediaWidgetViewModel _vm;
    private bool _editMode;
    private int _resizeEdge;
    private POINT _resizeStartCursor;
    private RECT _resizeStartRect;
    private (WidgetStyle, Color, double)? _appliedStyle;

    public MediaWidgetWindow(MediaWidgetViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        // 볼륨 믹서나 키보드로 바꾼 음량을 손대기 전에 다시 읽는다
        MouseEnter += (_, _) => _vm.RefreshVolume();
        // 시간 막대의 동그라미를 끄는 동안은 옮기지 않고, 놓을 때 한 번 옮긴다
        SeekBar.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _vm.BeginSeek()));
        SeekBar.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => _vm.EndSeek()));
    }

    /// <summary>이동이나 크기 조절을 마쳤을 때 (편집 모드).</summary>
    public event EventHandler? MovedOrResized;

    /// <param name="backgroundOpacity">배경만의 불투명도. 글자·버튼은 그대로.</param>
    public void ApplyStyle(WidgetStyle style, Color accent, double backgroundOpacity)
    {
        // Windows 설정 변경 알림은 자주 오는데 대부분 위젯과 상관없다. 같으면 다시 그리지 않는다.
        if (_appliedStyle == (style, accent, backgroundOpacity))
            return;
        _appliedStyle = (style, accent, backgroundOpacity);
        // 배경/테두리는 "불투명도 100%일 때"의 색. 유리 스타일은 100%여도 반투명. (파일 위젯과 같은 색)
        var (bg, border, text, subtle, hover, pressed, track) = style switch
        {
            WidgetStyle.Light => ("#FFF9FAFB", "#1F000000", "#FF1A1A1A", "#FF5F6672", "#0C000000", "#18000000", "#22000000"),
            WidgetStyle.Glass => ("#66000000", "#40FFFFFF", "#FFFFFFFF", "#FFD6DCE5", "#1AFFFFFF", "#2AFFFFFF", "#40FFFFFF"),
            _ => ("#FF1F2023", "#33FFFFFF", "#FFF3F4F6", "#FF9AA1AC", "#12FFFFFF", "#20FFFFFF", "#33FFFFFF"),
        };
        var opacity = Math.Clamp(backgroundOpacity, 0, 1);
        var background = ScaleAlpha(Parse(bg), opacity);
        // 완전히 투명한 픽셀은 클릭이 바탕화면으로 빠져나가므로 아주 조금은 남긴다 (눈에는 안 보임)
        if (background.A == 0)
            background.A = 1;

        SetBrush("MwBackground", background);
        SetBrush("MwBorder", ScaleAlpha(Parse(border), Math.Max(opacity, 0.3)));
        SetBrush("MwText", Parse(text));
        SetBrush("MwSubtle", Parse(subtle));
        SetBrush("MwHover", Parse(hover));
        SetBrush("MwPressed", Parse(pressed));
        SetBrush("MwTrack", Parse(track));
        SetBrush("MwArtBackground", Parse(hover));
        SetBrush("MwAccent", accent);
        // 강조색 위 글자: 밝은 강조색이면 검정
        var luminance = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255;
        SetBrush("MwOnAccent", luminance > 0.7 ? Colors.Black : Colors.White);
    }

    public void SetEditMode(bool enabled)
    {
        if (!enabled)
            EndResize();
        _editMode = enabled;
        EditOverlay.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Color ScaleAlpha(Color c, double factor) => Color.FromArgb((byte)Math.Round(c.A * factor), c.R, c.G, c.B);

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private void SetBrush(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Resources[key] = brush;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_editMode)
            DragMove(); // 끝나면 WM_EXITSIZEMOVE → MovedOrResized
    }

    /// <summary>위젯 위에서 휠을 굴리면 음량.</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_editMode || e.Delta == 0)
            return;
        _vm.NudgeVolume(Math.Sign(e.Delta) * Math.Max(1, Math.Abs(e.Delta) / Mouse.MouseWheelDeltaForOneLine));
        e.Handled = true;
    }

    // ================= 편집 모드: 크기 조절 =================

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST when _editMode:
                var hit = HitTestEdges(hwnd, lParam);
                if (hit != 0)
                {
                    handled = true; // Windows가 크기 조절 화살표 커서를 보여 준다
                    return hit;
                }
                break;
            case WM_NCLBUTTONDOWN when _editMode && wParam.ToInt32() is >= HTLEFT and <= HTBOTTOMRIGHT:
                BeginResize(hwnd, wParam.ToInt32() - (HTLEFT - WMSZ_LEFT));
                handled = true;
                break;
            case WM_MOUSEMOVE when _resizeEdge != 0:
                UpdateResize(hwnd);
                handled = true;
                break;
            case WM_LBUTTONUP when _resizeEdge != 0:
                EndResize();
                handled = true;
                break;
            case WM_CAPTURECHANGED when _resizeEdge != 0:
                EndResize(); // 다른 창이 마우스를 가져감 (Esc, 창 전환 등)
                break;
            case WM_EXITSIZEMOVE:
                MovedOrResized?.Invoke(this, EventArgs.Empty); // DragMove로 이동을 마침
                break;
        }
        return IntPtr.Zero;
    }

    private void BeginResize(IntPtr hwnd, int edge)
    {
        if (!GetCursorPos(out _resizeStartCursor) || !GetWindowRect(hwnd, out _resizeStartRect))
            return;
        _resizeEdge = edge;
        SetCapture(hwnd); // 버튼을 뗄 때까지 마우스 메시지를 이 창이 받는다
    }

    private void UpdateResize(IntPtr hwnd)
    {
        if (!GetCursorPos(out var cursor))
            return;
        var s = _resizeStartRect;
        var dpi = VisualTreeHelper.GetDpi(this);
        var r = ResizeRect((s.Left, s.Top, s.Right, s.Bottom), _resizeEdge,
            cursor.X - _resizeStartCursor.X, cursor.Y - _resizeStartCursor.Y,
            (int)Math.Ceiling(MediaWidgetLayout.MinWidth * dpi.DpiScaleX), (int)Math.Ceiling(MediaWidgetLayout.MinHeight * dpi.DpiScaleY));
        SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void EndResize()
    {
        if (_resizeEdge == 0)
            return;
        _resizeEdge = 0; // ReleaseCapture가 보내는 WM_CAPTURECHANGED에서 다시 들어오지 않도록 먼저
        ReleaseCapture();
        MovedOrResized?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>마우스가 가장자리에 있으면 그 방향의 크기 조절 위치(HT*)를 알려 준다.</summary>
    private int HitTestEdges(IntPtr hwnd, IntPtr lParam)
    {
        if (!GetWindowRect(hwnd, out var r))
            return 0;
        var p = lParam.ToInt64();
        int x = (short)(p & 0xFFFF), y = (short)((p >> 16) & 0xFFFF);
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var edge = EdgeSize * dpi;
        var corner = CornerSize * dpi;

        if (x >= r.Right - corner && y >= r.Bottom - corner)
            return HTBOTTOMRIGHT;
        bool left = x < r.Left + edge, right = x >= r.Right - edge, top = y < r.Top + edge, bottom = y >= r.Bottom - edge;
        return (top, bottom, left, right) switch
        {
            (true, _, true, _) => HTTOPLEFT,
            (true, _, _, true) => HTTOPRIGHT,
            (_, true, true, _) => HTBOTTOMLEFT,
            (_, true, _, true) => HTBOTTOMRIGHT,
            (true, _, _, _) => HTTOP,
            (_, true, _, _) => HTBOTTOM,
            (_, _, true, _) => HTLEFT,
            (_, _, _, true) => HTRIGHT,
            _ => 0,
        };
    }

    /// <summary>끄는 변만 움직이고 반대쪽 변은 제자리. 최소 크기보다 작아지지 않는다 (픽셀).</summary>
    private static (int Left, int Top, int Right, int Bottom) ResizeRect(
        (int Left, int Top, int Right, int Bottom) start, int edge, int dx, int dy, int minWidth, int minHeight)
    {
        bool left = edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT;
        bool right = edge is WMSZ_RIGHT or WMSZ_TOPRIGHT or WMSZ_BOTTOMRIGHT;
        bool top = edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT;
        bool bottom = edge is WMSZ_BOTTOM or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT;
        var (l, t, r, b) = start;
        if (left)
            l = Math.Min(start.Left + dx, start.Right - minWidth);
        if (right)
            r = Math.Max(start.Right + dx, start.Left + minWidth);
        if (top)
            t = Math.Min(start.Top + dy, start.Bottom - minHeight);
        if (bottom)
            b = Math.Max(start.Bottom + dy, start.Top + minHeight);
        return (l, t, r, b);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SetCapture(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
