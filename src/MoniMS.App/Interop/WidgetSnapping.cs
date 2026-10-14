using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MoniMS.Core.Presets;

namespace MoniMS.App.Interop;

/// <summary>
/// 위젯 자석 (<see cref="WidgetSnap"/>): 편집 모드에서 위젯을 끌 때 다른 위젯이나 화면 가장자리에 가까우면 가지런히 붙인다.
/// Windows가 끌기 중에 창을 옮기기 직전마다 보내는 WM_MOVING에서 갈 자리를 고친다.
/// 마우스를 더 멀리 끌면 다시 떨어진다.
/// </summary>
internal static partial class WidgetSnapping
{
    /// <summary>위젯 사이 간격 (DIP).</summary>
    public const double Gap = 12;

    /// <summary>화면 가장자리와의 간격 (DIP). 위젯을 처음 놓는 위치와 같다.</summary>
    public const double EdgeMargin = 24;

    /// <summary>이만큼 가까워지면 붙는다 (DIP).</summary>
    public const double Threshold = 16;

    private const int WM_MOVING = 0x0216;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>자석이 붙은 위젯 창 → 창 가장자리에서 보이는 카드까지의 투명한 여백 (DIP).</summary>
    private static readonly Dictionary<IntPtr, double> Widgets = [];

    /// <param name="visualInset">창 가장자리와 보이는 카드 사이의 투명한 여백 (DIP). 자석은 카드끼리 맞춘다.</param>
    public static void Attach(Window window, double visualInset = 0)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        Widgets[hwnd] = visualInset;
        HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
        window.Closed += (_, _) => Widgets.Remove(hwnd);
    }

    private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_MOVING || !Widgets.TryGetValue(hwnd, out var inset))
            return IntPtr.Zero;
        var proposed = Marshal.PtrToStructure<RECT>(lParam);
        var (dx, dy) = SnapOffset(hwnd, inset, proposed);
        if (dx == 0 && dy == 0)
            return IntPtr.Zero;
        var snapped = new RECT { Left = proposed.Left + dx, Top = proposed.Top + dy, Right = proposed.Right + dx, Bottom = proposed.Bottom + dy };
        Marshal.StructureToPtr(snapped, lParam, false);
        handled = true;
        return new IntPtr(1);
    }

    private static (int Dx, int Dy) SnapOffset(IntPtr hwnd, double inset, RECT proposed)
    {
        var scale = Scale(hwnd);
        var moving = ToSnap(proposed).Deflate(Px(inset, scale));

        var others = new List<SnapRect>();
        foreach (var (other, otherInset) in Widgets)
        {
            if (other != hwnd && IsWindowVisible(other) && GetWindowRect(other, out var r))
                others.Add(ToSnap(r).Deflate(Px(otherInset, Scale(other))));
        }

        var monitor = MonitorFromRect(ref proposed, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            return (0, 0);

        return WidgetSnap.Offset(moving, others, ToSnap(info.rcWork), Px(Gap, scale), Px(EdgeMargin, scale), Px(Threshold, scale));
    }

    private static double Scale(IntPtr hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96.0 : 1;
    }

    private static int Px(double dip, double scale) => (int)Math.Round(dip * scale);

    private static SnapRect ToSnap(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromRect(ref RECT rect, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
}
