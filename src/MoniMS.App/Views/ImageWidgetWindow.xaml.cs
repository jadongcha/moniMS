using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MoniMS.App.Imaging;
using MoniMS.App.Interop;
using MoniMS.Core.Presets;

namespace MoniMS.App.Views;

/// <summary>
/// 사진/GIF 위젯 창. 이미지는 창 크기에 맞춰 늘어나고 줄어든다.
/// 편집 모드에서는 안쪽을 끌면 이동, 가장자리를 끌면 크기 조절(이미지 비율 유지).
/// </summary>
/// <remarks>
/// 크기 조절은 Windows에 맡기지 않고 직접 한다. Windows의 크기 조절은 창에 WS_THICKFRAME이 있어야 동작하는데,
/// 그 스타일을 켜면 화면 가장자리로 끌 때 반쪽 화면/최대화로 스냅되어 비율이 깨진다.
/// </remarks>
public partial class ImageWidgetWindow : Window
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

    /// <summary>지금 끌고 있는 변 (WMSZ_*). 0이면 크기 조절 중이 아님.</summary>
    private int _resizeEdge;
    private POINT _resizeStartCursor;
    private RECT _resizeStartRect;

    /// <summary>크기 조절을 잡는 가장자리 폭 / 오른쪽 아래 모서리 크기 (DIP).</summary>
    private const double EdgeSize = 10;
    private const double CornerSize = 22;

    private static readonly Brush EditBackground = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)));

    private readonly ImageBrush _brush = new() { Stretch = Stretch.Fill };
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _rescaleTimer;
    private readonly DispatcherTimer _coverTimer;

    /// <summary>불러온 원본 (크기를 바꿀 때 다시 맞추는 기준).</summary>
    private LoadedImage? _image;

    /// <summary>실제로 보여주는 프레임 (애니메이션이면 창 픽셀 크기에 미리 맞춘 것).</summary>
    private LoadedImage? _display;

    private int _frame;
    private int _rescaleVersion;
    private bool _editMode;
    private bool _paused;
    private bool _covered;

    public ImageWidgetWindow()
    {
        InitializeComponent();
        Picture.Background = _brush;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => NextFrame();
        // 크기 조절이 끝나고 잠시 뒤 프레임을 새 크기로 다시 맞춘다 (조절하는 동안은 실시간 보간)
        _rescaleTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _rescaleTimer.Tick += (_, _) =>
        {
            _rescaleTimer.Stop();
            _ = RescaleAsync();
        };
        // 다른 창에 완전히 가려졌는지 가끔 확인 → 가려졌으면 재생을 멈춘다
        _coverTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _coverTimer.Tick += (_, _) => CheckCovered();
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        IsVisibleChanged += (_, _) => UpdateAnimation();
        SizeChanged += (_, _) => ScheduleRescale();
        DpiChanged += (_, _) => ScheduleRescale();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _rescaleTimer.Stop();
            _coverTimer.Stop();
        };
    }

    /// <summary>이동이나 크기 조절을 마쳤을 때 (편집 모드).</summary>
    public event EventHandler? MovedOrResized;

    /// <summary>이미지의 가로/세로 비율 (크기 조절 시 유지). 이미지가 없으면 0.</summary>
    public double AspectRatio => _image?.AspectRatio ?? 0;

    public bool HasImage => _image is not null;

    public void ShowImage(LoadedImage? image)
    {
        _timer.Stop();
        _image = image;
        _display = image;
        _frame = 0;
        _rescaleVersion++;
        _brush.ImageSource = image?.First;
        // 사진은 축소해도 선명한 고품질 보간. 애니메이션은 창 크기에 맞춘 프레임이 준비될 때까지 가벼운 보간.
        RenderOptions.SetBitmapScalingMode(Picture, image?.IsAnimated == true ? BitmapScalingMode.Linear : BitmapScalingMode.HighQuality);
        UpdateAnimation();
        if (image?.IsAnimated == true)
            ScheduleRescale();
    }

    private void ScheduleRescale()
    {
        if (_image is not { IsAnimated: true })
            return;
        RenderOptions.SetBitmapScalingMode(Picture, BitmapScalingMode.Linear);
        _rescaleTimer.Stop();
        _rescaleTimer.Start();
    }

    /// <summary>애니메이션 프레임을 지금 창의 픽셀 크기로 맞춘다 (백그라운드). 그러면 프레임마다 보간하지 않고 그대로 그린다.</summary>
    private async Task RescaleAsync()
    {
        if (_image is not { IsAnimated: true } image || ActualWidth <= 0)
            return;
        var dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Round(ActualWidth * dpi.DpiScaleX), height = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        var version = ++_rescaleVersion;
        var scaled = await Task.Run(() => ImageLoader.ScaleFrames(image, width, height));
        if (version != _rescaleVersion || !ReferenceEquals(image, _image))
            return; // 그사이 다른 이미지나 크기로 바뀜
        _display = scaled ?? image;
        _brush.ImageSource = _display.Frames[_frame];
        // 1:1이면 보간이 필요 없다. 메모리 때문에 못 맞췄으면 실시간 보간.
        RenderOptions.SetBitmapScalingMode(Picture, scaled is null ? BitmapScalingMode.Linear : BitmapScalingMode.NearestNeighbor);
    }

    public void ApplyAppearance(double opacity, double cornerRadius)
    {
        Picture.Opacity = Math.Clamp(opacity, 0.1, 1);
        var radius = new CornerRadius(Math.Max(0, cornerRadius));
        Picture.CornerRadius = radius;
        EditFrame.CornerRadius = radius;
    }

    public void SetEditMode(bool enabled, Color accent)
    {
        if (!enabled)
            EndResize();
        _editMode = enabled;
        var brush = Frozen(new SolidColorBrush(accent));
        EditFrame.BorderBrush = brush;
        EditLabel.Background = brush;
        EditGrip.Fill = brush;
        EditOverlay.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        Root.Background = enabled ? EditBackground : Brushes.Transparent;
    }

    /// <summary>화면 잠금 등으로 보이지 않을 때 애니메이션을 멈춘다.</summary>
    public void SetPaused(bool paused)
    {
        _paused = paused;
        UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        var active = _image is { IsAnimated: true } && IsVisible && !_paused;
        if (active && !_coverTimer.IsEnabled)
            _coverTimer.Start();
        else if (!active)
            _coverTimer.Stop();

        var run = active && !_covered;
        if (run && !_timer.IsEnabled)
        {
            _timer.Interval = _image!.Delays[_frame];
            _timer.Start();
        }
        else if (!run)
        {
            _timer.Stop();
        }
    }

    private void CheckCovered()
    {
        var covered = !_editMode && Occlusion.IsCovered(new WindowInteropHelper(this).Handle);
        if (covered == _covered)
            return;
        _covered = covered;
        UpdateAnimation();
    }

    private void NextFrame()
    {
        if (_display is not { IsAnimated: true } display)
            return;
        _frame = (_frame + 1) % display.Frames.Count;
        _brush.ImageSource = display.Frames[_frame];
        _timer.Interval = display.Delays[_frame];
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_editMode)
            DragMove(); // 끝나면 WM_EXITSIZEMOVE → MovedOrResized
    }

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
        var aspect = AspectRatio > 0 ? AspectRatio : (double)(s.Right - s.Left) / Math.Max(1, s.Bottom - s.Top);
        var r = ResizeRect((s.Left, s.Top, s.Right, s.Bottom), _resizeEdge,
            cursor.X - _resizeStartCursor.X, cursor.Y - _resizeStartCursor.Y,
            aspect, ImageWidgetLayout.MinSize * VisualTreeHelper.GetDpi(this).DpiScaleX);
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

    /// <summary>
    /// 끄기 시작한 사각형(start)과 마우스 이동량으로 새 사각형 (픽셀, 이미지 비율 유지).
    /// 좌·우 변은 너비, 위·아래 변은 높이를 따르고, 모서리는 가로·세로 중 더 많이 움직인 쪽을 따른다.
    /// 끄는 쪽의 반대 변은 제자리에 있고, 짧은 쪽은 minSide보다 작아지지 않는다.
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom) ResizeRect(
        (int Left, int Top, int Right, int Bottom) start, int edge, int dx, int dy, double aspect, double minSide)
    {
        bool left = edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT;
        bool right = edge is WMSZ_RIGHT or WMSZ_TOPRIGHT or WMSZ_BOTTOMRIGHT;
        bool top = edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT;
        bool bottom = edge is WMSZ_BOTTOM or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT;
        double w = Math.Max(1, start.Right - start.Left), h = Math.Max(1, start.Bottom - start.Top);
        var wantW = w + (right ? dx : left ? -dx : 0);
        var wantH = h + (bottom ? dy : top ? -dy : 0);

        double width;
        if (!left && !right)
            width = wantH * aspect;
        else if (!top && !bottom)
            width = wantW;
        else
            width = Math.Abs(wantW - w) / w >= Math.Abs(wantH - h) / h ? wantW : wantH * aspect;

        // 짧은 쪽이 minSide가 되는 너비보다 작아지지 않게 (반대쪽 변을 넘어 끌어 음수가 돼도)
        width = Math.Max(width, Math.Max(minSide, minSide * aspect));
        int newW = (int)Math.Round(width), newH = (int)Math.Round(width / aspect);
        var l = left ? start.Right - newW : start.Left;
        var t = top ? start.Bottom - newH : start.Top;
        return (l, t, l + newW, t + newH);
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
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
