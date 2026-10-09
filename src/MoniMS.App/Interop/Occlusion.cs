using System.Runtime.InteropServices;

namespace MoniMS.App.Interop;

/// <summary>
/// 바탕화면 위젯이 다른 창에 완전히 가려졌는지 (예: 최대화된 창, 전체 화면 게임).
/// 가려졌다고 잘못 판단하면 보이는 GIF가 멈추므로, 확실히 불투명한 일반 창 하나가 위젯을 통째로 덮을 때만 true.
/// </summary>
internal static partial class Occlusion
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20;
    private const long WS_EX_TOOLWINDOW = 0x80;
    private const long WS_EX_LAYERED = 0x80000;
    private const int DWMWA_CLOAKED = 14;

    public static bool IsCovered(IntPtr widget)
    {
        if (!GetWindowRect(widget, out var target))
            return false;
        var myPid = (uint)Environment.ProcessId;
        var covered = false;
        // EnumWindows는 위(앞)에서 아래 순서. 위젯보다 아래 창은 볼 필요가 없다.
        EnumWindows((h, _) =>
        {
            if (h == widget)
                return false;
            if (!IsWindowVisible(h) || IsIconic(h))
                return true;
            GetWindowThreadProcessId(h, out var pid);
            if (pid == myPid)
                return true; // 설정 창, 정보 위젯
            // 클릭 통과 오버레이, 반투명할 수 있는 창, 도구 창은 가린다고 보지 않는다
            var ex = (long)GetWindowLongPtr(h, GWL_EXSTYLE);
            if ((ex & (WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW)) != 0)
                return true;
            // 다른 가상 데스크톱 창 / 숨겨진 UWP 창
            if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;
            if (GetWindowRect(h, out var r) && r.Left <= target.Left && r.Top <= target.Top && r.Right >= target.Right && r.Bottom >= target.Bottom)
            {
                covered = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return covered;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    // 콜백(delegate)은 소스 생성 P/Invoke가 지원하지 않아서 DllImport
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
