using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MoniMS.App.Interop;

/// <summary>
/// 파일·폴더 아이콘과 표시 이름 (파일 탐색기와 같은 작은 아이콘).
/// 아이콘은 셸 확장이 그리기도 해서 느릴 수 있으므로 전용 STA 스레드에서 읽는다.
/// 실행 파일·바로 가기처럼 파일마다 다른 것만 파일별로, 나머지는 확장자별로 한 번만 읽는다.
/// </summary>
public sealed partial class ShellIcons : IDisposable
{
    /// <summary>파일마다 아이콘이 다른 확장자.</summary>
    private static readonly HashSet<string> PerFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".url", ".ico", ".cur", ".ani", ".msc", ".scr", ".cpl", ".appref-ms", ".website",
    };

    /// <summary>파일별 아이콘은 폴더를 돌아다닐수록 쌓이므로 이만큼 넘으면 비운다.</summary>
    private const int MaxPerFileIcons = 2000;

    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, ImageSource?> _typeIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageSource?> _fileIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Action<ImageSource?>>> _waiting = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>셸 스레드에서 할 일 (아이콘 읽기, 표시 이름 읽기).</summary>
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    private sealed record Request(string Key, string Path, bool PerFile, uint Attributes);

    public ShellIcons()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _thread = new Thread(Run) { IsBackground = true, Name = "MoniMS.ShellIcons" };
        _thread.SetApartmentState(ApartmentState.STA); // 셸 아이콘 처리기는 STA를 요구한다
        _thread.Start();
    }

    /// <summary>
    /// 이미 읽은 아이콘이면 바로 돌려준다. 아니면 null을 돌려주고, 읽은 뒤 UI 스레드에서 <paramref name="loaded"/>를 부른다.
    /// UI 스레드에서만 호출.
    /// </summary>
    public ImageSource? Get(string path, bool isFolder, FileAttributes attributes, Action<ImageSource?> loaded)
    {
        var request = CreateRequest(path, isFolder, attributes);
        var cache = request.PerFile ? _fileIcons : _typeIcons;
        if (cache.TryGetValue(request.Key, out var icon))
            return icon;
        if (_waiting.TryGetValue(request.Key, out var list))
        {
            list.Add(loaded);
            return null;
        }
        _waiting[request.Key] = [loaded];
        Enqueue(() =>
        {
            ImageSource? icon = null;
            try
            {
                icon = Load(request);
            }
            catch (Exception)
            {
                // 고장 난 셸 확장 등: 아이콘 없이 보여 준다
            }
            _dispatcher.BeginInvoke(DispatcherPriority.Background, () => Complete(request, icon));
        });
        return null;
    }

    /// <summary>
    /// 표시 이름을 셸 스레드에서 읽는다. 바탕 화면·문서처럼 이름이 바뀌어 보이는 폴더의 이름을 읽느라
    /// desktop.ini를 열기 때문에, 네트워크 폴더면 UI 스레드에서 부를 때 멈출 수 있다.
    /// </summary>
    public Task<string> GetDisplayNameAsync(string path)
    {
        var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Enqueue(() => result.TrySetResult(GetDisplayName(path))))
            result.TrySetResult(GetDisplayName(path));
        return result.Task;
    }

    /// <summary>앱을 끄는 중이면 false.</summary>
    private bool Enqueue(Action work)
    {
        if (_queue.IsAddingCompleted)
            return false;
        try
        {
            _queue.Add(work);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false; // 그사이 종료됨
        }
    }

    private static Request CreateRequest(string path, bool isFolder, FileAttributes attributes)
    {
        if (isFolder)
        {
            // 읽기 전용·시스템 속성 폴더는 desktop.ini로 아이콘을 바꿨을 수 있다 (문서, 다운로드, 드라이브 등)
            var custom = (attributes & (FileAttributes.ReadOnly | FileAttributes.System)) != 0 || Path.GetPathRoot(path) == path;
            return custom
                ? new Request(path, path, true, 0)
                : new Request("<folder>", "folder", false, FILE_ATTRIBUTE_DIRECTORY);
        }
        var ext = Path.GetExtension(path);
        return PerFileExtensions.Contains(ext)
            ? new Request(path, path, true, 0)
            : new Request("ext:" + ext, "file" + ext, false, FILE_ATTRIBUTE_NORMAL);
    }

    private void Run()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            try
            {
                work();
            }
            catch (Exception)
            {
                // 고장 난 셸 확장 등: 그 항목만 건너뛴다
            }
        }
    }

    private void Complete(Request request, ImageSource? icon)
    {
        if (request.PerFile)
        {
            if (_fileIcons.Count >= MaxPerFileIcons)
                _fileIcons.Clear();
            _fileIcons[request.Key] = icon;
        }
        else
        {
            _typeIcons[request.Key] = icon;
        }
        if (_waiting.Remove(request.Key, out var list))
        {
            foreach (var callback in list)
                callback(icon);
        }
    }

    private static unsafe BitmapSource? Load(Request request)
    {
        SHFILEINFOW info = default;
        var flags = SHGFI_ICON | SHGFI_SMALLICON | (request.PerFile ? SHGFI_LINKOVERLAY : SHGFI_USEFILEATTRIBUTES);
        if (SHGetFileInfoW(request.Path, request.Attributes, &info, (uint)sizeof(SHFILEINFOW), flags) == 0 || info.hIcon == IntPtr.Zero)
            return null;
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    /// <summary>파일 탐색기에 보이는 이름 (예: Documents → "문서", C:\ → "Local Disk (C:)"). 실패하면 폴더 이름.</summary>
    public static unsafe string GetDisplayName(string path)
    {
        SHFILEINFOW info = default;
        if (SHGetFileInfoW(path, 0, &info, (uint)sizeof(SHFILEINFOW), SHGFI_DISPLAYNAME) != 0)
        {
            var name = new string(info.szDisplayName);
            if (name.Length > 0)
                return name;
        }
        return FolderName(path);
    }

    /// <summary>경로의 마지막 부분 (디스크를 읽지 않는다). 드라이브 루트면 경로 그대로.</summary>
    public static string FolderName(string path)
    {
        var fileName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return fileName.Length > 0 ? fileName : path;
    }

    public void Dispose() => _queue.CompleteAdding();

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_DISPLAYNAME = 0x000000200;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint SHGFI_LINKOVERLAY = 0x000008000;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct SHFILEINFOW
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        public fixed char szDisplayName[260];
        public fixed char szTypeName[80];
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, SHFILEINFOW* psfi, uint cbFileInfo, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr hIcon);
}
