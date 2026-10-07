using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MoniMS.App.Interop;

/// <summary>창이 있는 모니터의 해상도(물리 픽셀)와 배율.</summary>
internal static partial class MonitorMetrics
{
    public readonly record struct Info(IntPtr Handle, int Width, int Height, double DpiScale);

    /// <summary>창이 걸쳐 있는 모니터 (아직 위치가 없으면 주 모니터).</summary>
    public static Info ForWindow(Window window, bool primary = false)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var monitor = primary || hwnd == IntPtr.Zero
            ? MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY)
            : MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            return default;

        double scale = 1;
        if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
            scale = dpiX / 96.0;
        return new Info(monitor, info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top, scale);
    }

    /// <summary>
    /// 창 크기를 DIP와 실제 픽셀 양쪽으로 확실히 맞춘다. 해상도·배율이 바뀌면 WPF가 예전 크기를 비례 조정해 덮어쓰는데,
    /// 계산 결과가 같으면 바인딩이 다시 적용되지 않아 창이 작게 남는 문제가 있어서 매번 직접 적용한다.
    /// </summary>
    public static void Resize(Window window, double width, double height, double dpiScale)
    {
        window.Width = width;
        window.Height = height;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || dpiScale <= 0)
            return;
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, (int)Math.Round(width * dpiScale), (int)Math.Round(height * dpiScale),
            SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(POINT pt, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
