using System.Runtime.InteropServices;
using MoniMS.Core.Interop;
using MoniMS.Core.Presets;

namespace MoniMS.Core.Desktop;

/// <summary>
/// 바탕화면 아이콘 위치 저장/복원.
/// 아이콘 ListView는 explorer.exe 프로세스 소유라서, 포인터를 주고받는 메시지는
/// explorer 메모리에 버퍼를 할당(VirtualAllocEx)해서 사용해야 한다.
/// </summary>
public sealed class DesktopIconService : IDesktopIconService
{
    private const int LVS_AUTOARRANGE = 0x0100;
    private const int GWL_STYLE = -16;
    private const int MaxText = 260;

    public DesktopIconLayout Capture()
    {
        using var remote = RemoteListView.Open();
        var layout = new DesktopIconLayout
        {
            ScreenWidth = GetSystemMetrics(SM_CXSCREEN),
            ScreenHeight = GetSystemMetrics(SM_CYSCREEN),
        };
        var count = remote.Count;
        for (var i = 0; i < count; i++)
        {
            var name = remote.GetText(i);
            var pt = remote.GetPosition(i);
            layout.Icons.Add(new IconPosition { Name = name, X = pt.X, Y = pt.Y });
        }
        return layout;
    }

    public IconApplyResult Apply(DesktopIconLayout layout)
    {
        using var remote = RemoteListView.Open();
        var style = (long)NativeMethods.GetWindowLongPtr(remote.Handle, GWL_STYLE);
        var autoArrange = (style & LVS_AUTOARRANGE) != 0;

        // 해상도가 바뀌었으면 비율로 보정
        int sw = GetSystemMetrics(SM_CXSCREEN), sh = GetSystemMetrics(SM_CYSCREEN);
        double sx = layout.ScreenWidth > 0 ? (double)sw / layout.ScreenWidth : 1;
        double sy = layout.ScreenHeight > 0 ? (double)sh / layout.ScreenHeight : 1;

        // 같은 이름이 여러 개일 수 있어서 이름별 큐로 순서대로 매칭
        var pending = layout.Icons
            .GroupBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => new Queue<IconPosition>(g), StringComparer.CurrentCultureIgnoreCase);

        var moved = 0;
        var count = remote.Count;
        for (var i = 0; i < count; i++)
        {
            var name = remote.GetText(i);
            if (!pending.TryGetValue(name, out var queue) || queue.Count == 0)
                continue;
            var target = queue.Dequeue();
            remote.SetPosition(i, (int)Math.Round(target.X * sx), (int)Math.Round(target.Y * sy));
            moved++;
        }

        NativeMethods.SendMessage(remote.Handle, NativeMethods.LVM_REDRAWITEMS, IntPtr.Zero, new IntPtr(count - 1));
        var missing = pending.Values.Sum(q => q.Count);
        return new IconApplyResult(moved, missing, autoArrange);
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    /// <summary>explorer 프로세스 안에 버퍼를 잡고 ListView 메시지를 보내는 헬퍼.</summary>
    private sealed unsafe class RemoteListView : IDisposable
    {
        private static readonly int ItemSize = Marshal.SizeOf<NativeMethods.LVITEMW>();
        private static readonly int BufferSize = ItemSize + MaxText * 2 + sizeof(NativeMethods.POINT);

        private readonly IntPtr _process;
        private readonly IntPtr _buffer;

        private RemoteListView(IntPtr hwnd, IntPtr process, IntPtr buffer)
        {
            Handle = hwnd;
            _process = process;
            _buffer = buffer;
        }

        public IntPtr Handle { get; }

        private IntPtr TextAddr => _buffer + ItemSize;
        private IntPtr PointAddr => _buffer + ItemSize + MaxText * 2;

        public int Count => (int)NativeMethods.SendMessage(Handle, NativeMethods.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);

        public static RemoteListView Open()
        {
            var hwnd = DesktopWindowFinder.FindIconListView();
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException("Could not find the desktop icon view.");

            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            var process = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_VM_WRITE, false, pid);
            if (process == IntPtr.Zero)
                throw new InvalidOperationException($"Failed to open explorer process (error {Marshal.GetLastPInvokeError()})");

            var buffer = NativeMethods.VirtualAllocEx(process, IntPtr.Zero, (nuint)BufferSize,
                NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE, NativeMethods.PAGE_READWRITE);
            if (buffer == IntPtr.Zero)
            {
                NativeMethods.CloseHandle(process);
                throw new InvalidOperationException("Remote memory allocation failed");
            }
            return new RemoteListView(hwnd, process, buffer);
        }

        public string GetText(int index)
        {
            var item = new NativeMethods.LVITEMW
            {
                mask = NativeMethods.LVIF_TEXT,
                iItem = index,
                pszText = TextAddr,
                cchTextMax = MaxText,
            };
            Write(_buffer, &item, ItemSize);
            var len = (int)NativeMethods.SendMessage(Handle, NativeMethods.LVM_GETITEMTEXTW, index, _buffer);
            if (len <= 0)
                return "";
            var chars = stackalloc char[MaxText];
            Read(TextAddr, chars, Math.Min(len, MaxText - 1) * 2);
            return new string(chars, 0, Math.Min(len, MaxText - 1));
        }

        public NativeMethods.POINT GetPosition(int index)
        {
            NativeMethods.SendMessage(Handle, NativeMethods.LVM_GETITEMPOSITION, index, PointAddr);
            NativeMethods.POINT pt;
            Read(PointAddr, &pt, sizeof(NativeMethods.POINT));
            return pt;
        }

        public void SetPosition(int index, int x, int y)
        {
            // MAKELPARAM: 포인터가 아니라 값이므로 원격 버퍼 불필요
            var lParam = (IntPtr)((y & 0xFFFF) << 16 | (x & 0xFFFF));
            NativeMethods.SendMessage(Handle, NativeMethods.LVM_SETITEMPOSITION, index, lParam);
        }

        private void Write(IntPtr address, void* data, int size)
        {
            if (!NativeMethods.WriteProcessMemory(_process, address, data, (nuint)size, out _))
                throw new InvalidOperationException("Remote memory write failed");
        }

        private void Read(IntPtr address, void* data, int size)
        {
            if (!NativeMethods.ReadProcessMemory(_process, address, data, (nuint)size, out _))
                throw new InvalidOperationException("Remote memory read failed");
        }

        public void Dispose()
        {
            NativeMethods.VirtualFreeEx(_process, _buffer, 0, NativeMethods.MEM_RELEASE);
            NativeMethods.CloseHandle(_process);
        }
    }
}
