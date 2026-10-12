using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using MoniMS.Core.Files;

namespace MoniMS.App.Interop;

/// <summary>
/// 파일 위젯에서 쓰는 Windows 셸 동작: 열기, 연결 프로그램, 속성, 복사·이동·삭제, 클립보드.
/// 복사·이동·삭제는 파일 탐색기와 같은 Windows 대화상자(진행률, 이름 충돌, 휴지통 확인)를 쓴다.
/// 오래 걸릴 수 있는 것은 별도 STA 스레드에서 실행해서 위젯(과 같은 UI 스레드의 다른 위젯)이 멈추지 않게 한다.
/// </summary>
internal static partial class ShellFiles
{
    private const string PreferredDropEffect = "Preferred DropEffect";
    private const int DropEffectCopy = 1;
    private const int DropEffectMove = 2;
    private const int DropEffectLink = 4;

    /// <summary>파일 탐색기 보기 설정: 숨김 항목 표시 / 보호된 운영 체제 파일 표시 / 휴지통으로 보낼 때 확인.</summary>
    public static (bool ShowHidden, bool ShowProtected, bool ConfirmRecycle) GetExplorerSettings()
    {
        var state = new byte[64]; // SHELLSTATE (첫 DWORD가 비트 필드)
        SHGetSetSettings(state, SSF_SHOWALLOBJECTS | SSF_SHOWSUPERHIDDEN | SSF_NOCONFIRMRECYCLE, false);
        var bits = BitConverter.ToUInt32(state, 0);
        return ((bits & (1u << 0)) != 0, (bits & (1u << 15)) != 0, (bits & (1u << 2)) == 0);
    }

    /// <summary>기본 프로그램으로 연다. 연결된 프로그램이 없으면 Windows가 "연결 프로그램" 창을 띄운다.</summary>
    public static void Open(string path, IntPtr owner, Action<Exception> onError) => RunSta("Open", () =>
    {
        using var _ = Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? "",
            ErrorDialog = true,
            ErrorDialogParentHandle = owner,
        });
    }, onError);

    /// <summary>"연결 프로그램 선택" 창.</summary>
    public static void OpenWith(string path, IntPtr owner, Action<Exception> onError) => RunSta("OpenWith", () =>
    {
        var info = new OPENASINFO { pcszFile = path, oaifInFlags = OAIF_ALLOW_REGISTRATION | OAIF_REGISTER_EXT | OAIF_EXEC };
        var hr = SHOpenWithDialog(owner, ref info);
        if (hr < 0 && hr != HRESULT_CANCELLED)
            Marshal.ThrowExceptionForHR(hr);
    }, onError);

    /// <summary>파일 탐색기에서 그 폴더를 연다.</summary>
    public static void OpenInExplorer(string folder, Action<Exception> onError) => RunSta("OpenInExplorer", () =>
    {
        using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }, onError);

    /// <summary>파일 탐색기에서 항목이 있는 폴더를 열고 그 항목을 선택한다.</summary>
    public static void ShowInExplorer(string path, Action<Exception> onError) => RunSta("ShowInExplorer", () =>
    {
        var pidl = ILCreateFromPathW(path);
        if (pidl == IntPtr.Zero)
            throw new FileNotFoundException("Item not found.", path);
        try
        {
            Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0));
        }
        finally
        {
            ILFree(pidl);
        }
    }, onError);

    /// <summary>
    /// 복사 또는 이동. 진행률·이름 충돌 창은 Windows가 띄운다. 끝나면(취소 포함) UI 스레드에서 onDone(성공 여부).
    /// 같은 폴더에 복사하면 Windows가 "- 복사본" 이름을 붙인다.
    /// </summary>
    public static void Transfer(IReadOnlyList<string> sources, string targetFolder, TransferAction action, IntPtr owner,
        Action<bool> onDone, Action<Exception> onError)
    {
        if (action == TransferAction.None || sources.Count == 0)
            return;
        ushort flags = FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR;
        if (action == TransferAction.Copy && FileTransferRules.AllIn(sources, targetFolder))
            flags |= FOF_RENAMEONCOLLISION;
        RunFileOperation(action == TransferAction.Move ? FO_MOVE : FO_COPY, sources, targetFolder, flags, owner, onDone, onError);
    }

    /// <summary>휴지통으로 (permanently면 영구 삭제, Windows가 항상 확인한다).</summary>
    public static void Delete(IReadOnlyList<string> paths, bool permanently, IntPtr owner, Action<bool> onDone, Action<Exception> onError)
    {
        ushort flags = permanently ? (ushort)0 : FOF_ALLOWUNDO;
        // 휴지통으로 보낼 때는 파일 탐색기의 "삭제 확인 대화 상자 표시" 설정을 따른다
        if (!permanently && !GetExplorerSettings().ConfirmRecycle)
            flags |= FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING;
        RunFileOperation(FO_DELETE, paths, null, flags, owner, onDone, onError);
    }

    private static void RunFileOperation(uint func, IReadOnlyList<string> sources, string? target, ushort flags, IntPtr owner,
        Action<bool> onDone, Action<Exception> onError)
    {
        var dispatcher = Application.Current.Dispatcher;
        RunSta("FileOperation", () =>
        {
            var from = Marshal.StringToHGlobalUni(string.Join('\0', sources) + "\0\0");
            var to = target is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(target + "\0\0");
            try
            {
                var op = new SHFILEOPSTRUCTW { hwnd = owner, wFunc = func, pFrom = from, pTo = to, fFlags = flags };
                var result = SHFileOperationW(ref op);
                // 0이 아니어도 Windows가 이미 오류를 보여 준 경우가 대부분이라 결과만 알린다
                var ok = result == 0 && !op.fAnyOperationsAborted;
                dispatcher.BeginInvoke(() => onDone(ok));
            }
            finally
            {
                Marshal.FreeHGlobal(from);
                if (to != IntPtr.Zero)
                    Marshal.FreeHGlobal(to);
            }
        }, onError);
    }

    /// <summary>이름 바꾸기 (같은 폴더 안). 대소문자만 바꾸는 것도 된다.</summary>
    /// <exception cref="IOException">이미 같은 이름이 있거나 사용 중 등</exception>
    public static string Rename(string path, string newName)
    {
        var target = Path.Combine(Path.GetDirectoryName(path)!, newName);
        if (!MoveFileExW(path, target, 0))
        {
            var error = Marshal.GetLastPInvokeError();
            throw new IOException(error switch
            {
                ERROR_ALREADY_EXISTS or ERROR_FILE_EXISTS => $"There is already a file or folder named '{newName}' here.",
                ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION => "The file is open in another program. Close it and try again.",
                ERROR_ACCESS_DENIED => "You don't have permission to rename this item.",
                ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND => "The item no longer exists.",
                _ => new System.ComponentModel.Win32Exception(error).Message,
            });
        }
        return target;
    }

    /// <summary>복사·잘라내기: 파일 탐색기에 붙여 넣어도 된다.</summary>
    public static void SetClipboard(IReadOnlyList<string> paths, bool cut)
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths.ToArray());
        data.SetData(PreferredDropEffect, new MemoryStream(BitConverter.GetBytes(cut ? DropEffectMove : DropEffectCopy | DropEffectLink)));
        RetryClipboard(() => Clipboard.SetDataObject(data, copy: true));
    }

    /// <summary>클립보드의 파일 (없으면 빈 목록) 과 잘라내기였는지.</summary>
    public static (IReadOnlyList<string> Paths, bool Cut) GetClipboard()
    {
        IDataObject? data = null;
        RetryClipboard(() => data = Clipboard.GetDataObject());
        if (data?.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
            return ([], false);
        var cut = false;
        if (data.GetData(PreferredDropEffect) is MemoryStream stream && stream.Length >= 4)
        {
            var bytes = new byte[4];
            stream.ReadExactly(bytes);
            var effect = BitConverter.ToInt32(bytes, 0);
            cut = (effect & DropEffectMove) != 0 && (effect & DropEffectCopy) == 0;
        }
        return (paths, cut);
    }

    public static bool ClipboardHasFiles()
    {
        var has = false;
        try
        {
            RetryClipboard(() => has = Clipboard.ContainsFileDropList());
        }
        catch (ExternalException)
        {
        }
        return has;
    }

    /// <summary>잘라낸 파일을 붙여 넣은 뒤 (파일 탐색기처럼 클립보드를 비운다). 그사이 다른 것을 복사했으면 그대로 둔다.</summary>
    public static void ClearClipboardIfStill(IReadOnlyList<string> paths)
    {
        try
        {
            var (current, cut) = GetClipboard();
            if (cut && current.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase))
                RetryClipboard(Clipboard.Clear);
        }
        catch (ExternalException)
        {
        }
    }

    /// <summary>"경로로 복사": 따옴표로 감싼 전체 경로, 여러 개면 줄마다.</summary>
    public static void CopyPaths(IEnumerable<string> paths) =>
        RetryClipboard(() => Clipboard.SetText(string.Join(Environment.NewLine, paths.Select(p => $"\"{p}\""))));

    /// <summary>다른 프로그램이 클립보드를 잠깐 열고 있으면 실패하므로 몇 번 다시 시도한다.</summary>
    private static void RetryClipboard(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (ExternalException) when (attempt < 4)
            {
                Thread.Sleep(30);
            }
        }
    }

    /// <summary>셸 호출은 STA가 필요하고 오래 걸릴 수 있다 (네트워크 드라이브, 진행률 창 등). 실패는 UI 스레드로 알린다.</summary>
    private static void RunSta(string name, Action action, Action<Exception> onError)
    {
        var dispatcher = Application.Current.Dispatcher;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                dispatcher.BeginInvoke(() => onError(ex));
            }
        })
        {
            IsBackground = true,
            Name = "MoniMS.Shell." + name,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    // ---------- Win32 ----------

    private const uint SSF_SHOWALLOBJECTS = 0x00000001;
    private const uint SSF_NOCONFIRMRECYCLE = 0x00008000;
    private const uint SSF_SHOWSUPERHIDDEN = 0x00040000;

    private const uint FO_MOVE = 1;
    private const uint FO_COPY = 2;
    private const uint FO_DELETE = 3;
    private const ushort FOF_RENAMEONCOLLISION = 0x0008;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMMKDIR = 0x0200;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    private const int OAIF_ALLOW_REGISTRATION = 0x1;
    private const int OAIF_REGISTER_EXT = 0x2;
    private const int OAIF_EXEC = 0x4;
    private const int HRESULT_CANCELLED = unchecked((int)0x800704C7);

    private const int ERROR_FILE_NOT_FOUND = 2;
    private const int ERROR_PATH_NOT_FOUND = 3;
    private const int ERROR_ACCESS_DENIED = 5;
    private const int ERROR_SHARING_VIOLATION = 32;
    private const int ERROR_LOCK_VIOLATION = 33;
    private const int ERROR_FILE_EXISTS = 80;
    private const int ERROR_ALREADY_EXISTS = 183;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENASINFO
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pcszFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pcszClass;
        public int oaifInFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW lpFileOp);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHOpenWithDialog(IntPtr hwndParent, ref OPENASINFO poainfo);

    [LibraryImport("shell32.dll")]
    private static partial void SHGetSetSettings([In, Out] byte[] lpss, uint dwMask, [MarshalAs(UnmanagedType.Bool)] bool bSet);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr ILCreateFromPathW(string pszPath);

    [LibraryImport("shell32.dll")]
    private static partial void ILFree(IntPtr pidl);

    [LibraryImport("shell32.dll")]
    private static partial int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr apidl, uint dwFlags);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFileExW(string existing, string newName, uint flags);
}
