using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MoniMS.Core.Desktop;

namespace MoniMS.App.Interop;

/// <summary>
/// 위젯 창을 "바탕화면에 붙은" 상태로 만든다.
/// - 소유자(owner)를 Progman(바탕화면)으로 설정 → Win+D(바탕화면 보기)에도 사라지지 않음
/// - 항상 Z순서 맨 아래(HWND_BOTTOM) 유지 → 다른 창을 가리지 않음
/// - 작업표시줄/Alt+Tab에 표시 안 함, 포커스를 빼앗지 않음
/// </summary>
internal static partial class DesktopPinning
{
    private const int GWL_EXSTYLE = -20;
    private const int GWLP_HWNDPARENT = -8;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_APPWINDOW = 0x00040000;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const long WS_EX_LAYERED = 0x00080000;
    private const int WM_STYLECHANGING = 0x007C;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct STYLESTRUCT
    {
        public uint styleOld;
        public uint styleNew;
    }

    /// <summary>창별로 원하는 클릭 통과 상태. WPF가 확장 스타일을 다시 쓰더라도 이 값으로 강제한다.</summary>
    private static readonly Dictionary<IntPtr, bool> ClickThroughState = new();
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_BOTTOM = new(1);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessage(string name);

    /// <summary>explorer.exe가 재시작되면 브로드캐스트되는 메시지.</summary>
    public static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

    public static void Pin(Window window, Action? onExplorerRestart = null)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        ex = (ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)ex);

        var progman = DesktopWindowFinder.Progman;
        if (progman != IntPtr.Zero)
            SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, progman);

        SendToBottom(hwnd);

        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook((IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg == WM_WINDOWPOSCHANGING)
            {
                // 누가 위로 올리려고 해도 항상 맨 아래로
                var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                if ((pos.flags & SWP_NOZORDER) == 0)
                {
                    pos.hwndInsertAfter = HWND_BOTTOM;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
            }
            else if (msg == WM_STYLECHANGING && wParam.ToInt64() == GWL_EXSTYLE)
            {
                // WPF가 내부 캐시로 확장 스타일을 덮어써 WS_EX_TRANSPARENT가 빠지는 경우를 막는다
                var ss = Marshal.PtrToStructure<STYLESTRUCT>(lParam);
                var desired = EnforceExStyle(h, ss.styleNew);
                if (desired != ss.styleNew)
                {
                    ss.styleNew = desired;
                    Marshal.StructureToPtr(ss, lParam, false);
                }
            }
            else if (msg == TaskbarCreatedMessage && onExplorerRestart is not null)
            {
                onExplorerRestart();
            }
            return IntPtr.Zero;
        });
    }

    public static void SendToBottom(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>
    /// 클릭 통과: WS_EX_LAYERED + WS_EX_TRANSPARENT 조합이면 마우스 입력이 아래 창(바탕화면 아이콘)으로 간다.
    /// </summary>
    /// <returns>적용 후 실제 확장 스타일 (진단용).</returns>
    public static long SetClickThrough(Window window, bool enabled)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return 0;
        ClickThroughState[hwnd] = enabled;
        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)EnforceExStyle(hwnd, (uint)ex));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        return (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
    }

    public static bool IsClickThrough(IntPtr exStyle) => ((long)exStyle & WS_EX_TRANSPARENT) != 0;

    private static uint EnforceExStyle(IntPtr hwnd, uint style)
    {
        long s = style;
        s = (s | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED) & ~WS_EX_APPWINDOW;
        s = ClickThroughState.TryGetValue(hwnd, out var ct) && ct ? s | WS_EX_TRANSPARENT : s & ~WS_EX_TRANSPARENT;
        return (uint)s;
    }
}
