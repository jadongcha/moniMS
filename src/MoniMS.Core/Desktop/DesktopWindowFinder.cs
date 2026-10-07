using MoniMS.Core.Interop;

namespace MoniMS.Core.Desktop;

/// <summary>바탕화면 관련 창 핸들 찾기 (Windows 11 23H2/24H2 구조 모두 대응).</summary>
public static class DesktopWindowFinder
{
    public static IntPtr Progman => NativeMethods.FindWindow("Progman", null);

    /// <summary>바탕화면 아이콘을 그리는 ListView(SysListView32 "FolderView").</summary>
    public static IntPtr FindIconListView()
    {
        var defView = FindDefView();
        return defView == IntPtr.Zero ? IntPtr.Zero : NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
    }

    /// <summary>SHELLDLL_DefView는 보통 Progman 아래, 배경 슬라이드쇼 등을 쓰면 WorkerW 아래로 옮겨진다.</summary>
    public static IntPtr FindDefView()
    {
        var defView = NativeMethods.FindWindowEx(Progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView != IntPtr.Zero)
            return defView;

        var found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            var dv = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (dv != IntPtr.Zero)
            {
                found = dv;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
