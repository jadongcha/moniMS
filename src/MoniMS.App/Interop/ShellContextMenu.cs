using System.Runtime.InteropServices;
using System.Windows.Input;

namespace MoniMS.App.Interop;

/// <summary>
/// Windows의 원래 우클릭 메뉴 ("Show more options"): 다른 프로그램이 넣은 항목(압축, 보내기, 연결 프로그램 등)까지 모두 나온다.
/// 파일 탐색기와 같은 IContextMenu를 쓴다. 메뉴가 열려 있는 동안 창은 <see cref="HandleMessage"/>로
/// 하위 메뉴 그리기 메시지를 넘겨 줘야 아이콘·"새로 만들기" 같은 하위 메뉴가 제대로 나온다.
/// </summary>
internal static partial class ShellContextMenu
{
    private const uint FirstCommand = 1;
    private const uint LastCommand = 0x7FFF;

    /// <summary>지금 열려 있는 메뉴 (하위 메뉴 메시지 전달용). UI 스레드에서만 쓴다.</summary>
    private static IContextMenu2? _open2;
    private static IContextMenu3? _open3;

    /// <summary>같은 폴더에 있는 항목들의 메뉴.</summary>
    /// <param name="x">화면 좌표 (물리 픽셀)</param>
    /// <param name="handleVerb">직접 처리할 동작 (예: "rename"). true를 돌려주면 Windows에 넘기지 않는다.</param>
    public static void ShowForItems(IntPtr hwnd, IReadOnlyList<string> paths, int x, int y, bool extended, Func<string, bool>? handleVerb)
    {
        var pidls = new List<IntPtr>();
        IContextMenu? menu = null;
        try
        {
            menu = CreateForItems(hwnd, paths, pidls);
            Show(hwnd, menu, x, y, extended, Path.GetDirectoryName(paths[0]), handleVerb);
        }
        finally
        {
            Release(menu, pidls);
        }
    }

    /// <summary>폴더 빈 곳의 메뉴 (새로 만들기, 붙여넣기, 터미널에서 열기 등).</summary>
    public static void ShowForFolder(IntPtr hwnd, string folder, int x, int y, bool extended)
    {
        var pidls = new List<IntPtr>();
        IContextMenu? menu = null;
        try
        {
            menu = CreateForFolder(hwnd, folder, pidls);
            Show(hwnd, menu, x, y, extended, folder, null);
        }
        finally
        {
            Release(menu, pidls);
        }
    }

    /// <summary>메뉴를 띄우지 않고 동작 하나를 실행 (예: 여러 항목의 "properties").</summary>
    public static void InvokeVerb(IntPtr hwnd, IReadOnlyList<string> paths, string verb)
    {
        var pidls = new List<IntPtr>();
        IContextMenu? menu = null;
        var hmenu = CreatePopupMenu();
        try
        {
            menu = CreateForItems(hwnd, paths, pidls);
            Check(menu.QueryContextMenu(hmenu, 0, FirstCommand, LastCommand, CMF_NORMAL));
            var verbA = Marshal.StringToHGlobalAnsi(verb);
            var verbW = Marshal.StringToHGlobalUni(verb);
            try
            {
                Invoke(menu, hwnd, verbA, verbW, 0, 0, Path.GetDirectoryName(paths[0]), usePoint: false);
            }
            finally
            {
                Marshal.FreeHGlobal(verbA);
                Marshal.FreeHGlobal(verbW);
            }
        }
        finally
        {
            DestroyMenu(hmenu);
            Release(menu, pidls);
        }
    }

    /// <summary>창 프로시저에서 호출. 셸 메뉴가 열려 있을 때 그 메뉴의 그리기 메시지를 넘긴다.</summary>
    public static bool HandleMessage(int msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (msg is not (WM_INITMENUPOPUP or WM_DRAWITEM or WM_MEASUREITEM or WM_MENUCHAR))
            return false;
        try
        {
            if (_open3 is not null)
                return _open3.HandleMenuMsg2((uint)msg, wParam, lParam, out result) >= 0;
            if (_open2 is not null && msg != WM_MENUCHAR && _open2.HandleMenuMsg((uint)msg, wParam, lParam) >= 0)
            {
                result = msg == WM_INITMENUPOPUP ? IntPtr.Zero : new IntPtr(1);
                return true;
            }
        }
        catch (Exception)
        {
            // 셸 확장 오류: 메뉴 그리기만 실패하게 둔다
        }
        return false;
    }

    private static void Show(IntPtr hwnd, IContextMenu menu, int x, int y, bool extended, string? directory, Func<string, bool>? handleVerb)
    {
        var hmenu = CreatePopupMenu();
        try
        {
            var flags = CMF_NORMAL | (handleVerb is null ? 0 : CMF_CANRENAME) | (extended ? CMF_EXTENDEDVERBS : 0);
            Check(menu.QueryContextMenu(hmenu, 0, FirstCommand, LastCommand, flags));
            _open2 = menu as IContextMenu2;
            _open3 = menu as IContextMenu3;
            uint command;
            try
            {
                SetForegroundWindow(hwnd); // 메뉴 밖을 누르면 닫히도록
                command = (uint)TrackPopupMenuEx(hmenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, hwnd, IntPtr.Zero);
                PostMessageW(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                _open2 = null;
                _open3 = null;
            }
            if (command < FirstCommand)
                return;
            var offset = (IntPtr)(command - FirstCommand);
            if (handleVerb is not null && GetVerb(menu, offset) is { } verb && handleVerb(verb))
                return;
            Invoke(menu, hwnd, offset, offset, x, y, directory, usePoint: true);
        }
        finally
        {
            DestroyMenu(hmenu);
        }
    }

    private static string? GetVerb(IContextMenu menu, IntPtr offset)
    {
        const int size = 256;
        var buffer = Marshal.AllocHGlobal(size * 2);
        try
        {
            Marshal.WriteInt16(buffer, 0);
            return menu.GetCommandString((UIntPtr)(ulong)offset, GCS_VERBW, IntPtr.Zero, buffer, size) >= 0
                ? Marshal.PtrToStringUni(buffer)
                : null;
        }
        catch (Exception)
        {
            return null; // 동작 이름이 없는 항목 (셸 확장)
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void Invoke(IContextMenu menu, IntPtr hwnd, IntPtr verbA, IntPtr verbW, int x, int y, string? directory, bool usePoint)
    {
        var dirA = directory is null ? IntPtr.Zero : Marshal.StringToHGlobalAnsi(directory);
        var dirW = directory is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(directory);
        try
        {
            var mods = Keyboard.Modifiers;
            var info = new CMINVOKECOMMANDINFOEX
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = CMIC_MASK_UNICODE
                    | (usePoint ? CMIC_MASK_PTINVOKE : 0)
                    | (mods.HasFlag(ModifierKeys.Control) ? CMIC_MASK_CONTROL_DOWN : 0)
                    | (mods.HasFlag(ModifierKeys.Shift) ? CMIC_MASK_SHIFT_DOWN : 0),
                hwnd = hwnd,
                lpVerb = verbA,
                lpVerbW = verbW,
                lpDirectory = dirA,
                lpDirectoryW = dirW,
                nShow = SW_SHOWNORMAL,
                ptInvoke = new POINT { X = x, Y = y },
            };
            Check(menu.InvokeCommand(ref info));
        }
        finally
        {
            if (dirA != IntPtr.Zero)
                Marshal.FreeHGlobal(dirA);
            if (dirW != IntPtr.Zero)
                Marshal.FreeHGlobal(dirW);
        }
    }

    private static IContextMenu CreateForItems(IntPtr hwnd, IReadOnlyList<string> paths, List<IntPtr> pidls)
    {
        var children = new IntPtr[paths.Count];
        IntPtr parentPtr = IntPtr.Zero;
        for (var i = 0; i < paths.Count; i++)
        {
            var pidl = Parse(paths[i]);
            pidls.Add(pidl);
            if (i == 0)
                Check(SHBindToParent(pidl, IID_IShellFolder, out parentPtr, out children[0]));
            else
                children[i] = ILFindLastID(pidl); // 같은 폴더 안이므로 마지막 부분만
        }
        var parent = (IShellFolder)Marshal.GetObjectForIUnknown(parentPtr);
        Marshal.Release(parentPtr);
        try
        {
            var iid = IID_IContextMenu;
            Check(parent.GetUIObjectOf(hwnd, (uint)children.Length, children, ref iid, IntPtr.Zero, out var menuPtr));
            return ToMenu(menuPtr);
        }
        finally
        {
            Marshal.FinalReleaseComObject(parent);
        }
    }

    private static IContextMenu CreateForFolder(IntPtr hwnd, string folder, List<IntPtr> pidls)
    {
        var pidl = Parse(folder);
        pidls.Add(pidl);
        Check(SHGetDesktopFolder(out var desktopPtr));
        var desktop = (IShellFolder)Marshal.GetObjectForIUnknown(desktopPtr);
        Marshal.Release(desktopPtr);
        IShellFolder? target = null;
        try
        {
            var iidFolder = IID_IShellFolder;
            Check(desktop.BindToObject(pidl, IntPtr.Zero, ref iidFolder, out var folderPtr));
            target = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);
            Marshal.Release(folderPtr);
            var iidMenu = IID_IContextMenu;
            Check(target.CreateViewObject(hwnd, ref iidMenu, out var menuPtr));
            return ToMenu(menuPtr);
        }
        finally
        {
            if (target is not null)
                Marshal.FinalReleaseComObject(target);
            Marshal.FinalReleaseComObject(desktop);
        }
    }

    private static IContextMenu ToMenu(IntPtr ptr)
    {
        var menu = (IContextMenu)Marshal.GetObjectForIUnknown(ptr);
        Marshal.Release(ptr);
        return menu;
    }

    private static IntPtr Parse(string path)
    {
        Check(SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _));
        return pidl;
    }

    private static void Release(IContextMenu? menu, List<IntPtr> pidls)
    {
        if (menu is not null)
            Marshal.FinalReleaseComObject(menu);
        foreach (var pidl in pidls)
            ILFree(pidl);
    }

    private static void Check(int hr)
    {
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    // ---------- Win32 / COM ----------

    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    private const uint CMF_NORMAL = 0x0;
    private const uint CMF_CANRENAME = 0x10;
    private const uint CMF_EXTENDEDVERBS = 0x100;
    private const uint GCS_VERBW = 0x4;
    private const uint CMIC_MASK_UNICODE = 0x4000;
    private const uint CMIC_MASK_SHIFT_DOWN = 0x10000000;
    private const uint CMIC_MASK_PTINVOKE = 0x20000000;
    private const uint CMIC_MASK_CONTROL_DOWN = 0x40000000;
    private const int SW_SHOWNORMAL = 1;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const int WM_NULL = 0x0000;
    private const int WM_DRAWITEM = 0x002B;
    private const int WM_MEASUREITEM = 0x002C;
    private const int WM_INITMENUPOPUP = 0x0117;
    private const int WM_MENUCHAR = 0x0120;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public POINT ptInvoke;
    }

    /// <remarks>메서드 순서가 vtable 순서라 바꾸면 안 된다.</remarks>
    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr pchEaten, out IntPtr ppidl, IntPtr pdwAttributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IntPtr ppenumIDList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr pName);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr ppidlOut);
    }

    [ComImport]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
    }

    [ComImport]
    [Guid("000214F4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport]
    [Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [LibraryImport("shell32.dll")]
    private static partial int SHBindToParent(IntPtr pidl, in Guid riid, out IntPtr ppv, out IntPtr ppidlLast);

    [LibraryImport("shell32.dll")]
    private static partial int SHGetDesktopFolder(out IntPtr ppshf);

    [LibraryImport("shell32.dll")]
    private static partial IntPtr ILFindLastID(IntPtr pidl);

    [LibraryImport("shell32.dll")]
    private static partial void ILFree(IntPtr pidl);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CreatePopupMenu();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(IntPtr hmenu);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(IntPtr hmenu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
