using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MoniMS.App.Interop;
using MoniMS.Core.Files;

namespace MoniMS.App.ViewModels;

/// <summary>파일 위젯 목록의 한 줄 (파일 또는 폴더).</summary>
public sealed partial class FileItemViewModel : ObservableObject
{
    private readonly ShellIcons _icons;
    private ImageSource? _icon;
    private bool _iconRequested;

    public FileItemViewModel(FileEntry entry, ShellIcons icons)
    {
        Entry = entry;
        _icons = icons;
    }

    public FileEntry Entry { get; private set; }
    public string Path => Entry.FullPath;
    public string Name => Entry.Name;
    public string DisplayName => Entry.BaseName;
    public string Extension => Entry.Extension;
    public bool IsFolder => Entry.IsFolder;
    public bool IsHidden => Entry.IsHidden;

    /// <summary>화면에 보일 때 처음 읽는다 (목록은 보이는 줄만 만들므로 큰 폴더도 빠르다).</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                _icon = _icons.Get(Entry.FullPath, Entry.IsFolder, Entry.Attributes, icon =>
                {
                    _icon = icon;
                    OnPropertyChanged(nameof(Icon));
                });
            }
            return _icon;
        }
    }

    /// <summary>이름을 바꾸는 중 (이름 칸이 입력 상자로 바뀐다).</summary>
    [ObservableProperty] private bool _isRenaming;

    /// <summary>파일을 끌어 이 폴더 위에 올려 둔 상태.</summary>
    [ObservableProperty] private bool _isDropTarget;

    /// <summary>다시 읽었을 때 같은 항목인데 속성(숨김 등)이나 이름의 대소문자만 바뀐 경우 (a.txt → A.txt).</summary>
    internal void Update(FileEntry entry)
    {
        if (entry.Attributes == Entry.Attributes && string.Equals(entry.FullPath, Entry.FullPath, StringComparison.Ordinal))
            return;
        Entry = entry;
        OnPropertyChanged(string.Empty); // 모든 표시 값
    }
}

/// <summary>
/// 왼쪽 목록의 폴더 (사용자가 고른 폴더). 있는지와 표시 이름(문서, 바탕 화면 …)은 백그라운드에서 알아낸다.
/// 연결이 끊긴 네트워크 폴더는 확인에 수십 초가 걸려서 UI 스레드에서 하면 앱 전체가 멈춘다.
/// </summary>
public sealed partial class SidebarFolderViewModel : ObservableObject
{
    private readonly ShellIcons _icons;
    private ImageSource? _icon;
    private bool _iconRequested;
    private bool _checked;
    private FileAttributes _attributes = FileAttributes.Directory;

    public SidebarFolderViewModel(string path, ShellIcons icons)
    {
        Path = path;
        _icons = icons;
        _name = ShellIcons.FolderName(path);
        _ = CheckAsync();
    }

    public string Path { get; }

    [ObservableProperty] private string _name;

    /// <summary>확인하기 전에는 있다고 본다 (흐리게 보였다가 돌아오지 않도록).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    private bool _exists = true;

    public string ToolTip => Exists ? Path : Path + "\n(not available)";

    public ImageSource? Icon
    {
        get
        {
            if (!_iconRequested && _checked && Exists)
            {
                _iconRequested = true;
                _icon = _icons.Get(Path, true, _attributes, icon =>
                {
                    _icon = icon;
                    OnPropertyChanged(nameof(Icon));
                });
            }
            return _icon;
        }
    }

    [ObservableProperty] private bool _isDropTarget;

    private async Task CheckAsync()
    {
        var attributes = await Task.Run(() =>
        {
            try
            {
                return (FileAttributes?)File.GetAttributes(Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        });
        _checked = true;
        Exists = attributes is { } a && (a & FileAttributes.Directory) != 0;
        if (!Exists)
            return;
        _attributes = attributes!.Value;
        OnPropertyChanged(nameof(Icon)); // 이제 아이콘을 읽는다
        Name = await _icons.GetDisplayNameAsync(Path);
    }
}

/// <summary>주소 표시줄의 한 칸.</summary>
public sealed record BreadcrumbViewModel(string Name, string Path, bool IsFirst);

/// <summary>목록을 다시 읽은 뒤 창이 할 일 (선택·스크롤·이름 바꾸기 시작).</summary>
public sealed class ItemsLoadedEventArgs(IReadOnlyList<string> select, bool rename, bool sameFolder) : EventArgs
{
    /// <summary>선택할 항목. 비어 있으면 같은 폴더일 때는 선택을 유지하고, 새 폴더면 선택 없음.</summary>
    public IReadOnlyList<string> Select { get; } = select;

    /// <summary>선택한 항목의 이름 바꾸기를 바로 시작 (새 폴더).</summary>
    public bool Rename { get; } = rename;

    /// <summary>같은 폴더를 다시 읽음 (스크롤 위치 유지).</summary>
    public bool SameFolder { get; } = sameFolder;
}

/// <summary>
/// 파일 위젯 화면: 왼쪽 폴더 목록, 주소 표시줄, 폴더 내용(이름 · 확장자), 상태 표시줄.
/// 폴더 내용은 백그라운드에서 읽고, 폴더가 바뀌면(파일 추가·삭제·이름 변경) 자동으로 다시 읽는다.
/// 파일을 다루는 동작(열기, 복사, 이름 바꾸기 …)은 <see cref="Views.FileWidgetWindow"/>에서 이 클래스와 ShellFiles를 써서 한다.
/// </summary>
public sealed partial class FileWidgetViewModel : ObservableObject, IDisposable
{
    private readonly ShellIcons _icons;
    private readonly ILogger<FileWidgetViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();

    /// <summary>파일이 바뀐 알림이 몰려 와도 한 번만 다시 읽도록 잠깐 기다린다.</summary>
    private readonly DispatcherTimer _reloadTimer;

    private FileSystemWatcher? _watcher;
    private string? _loadedPath;
    private (FileSortColumn, bool) _loadedSort;
    private int _loadVersion;
    private bool _active;

    public FileWidgetViewModel(ShellIcons icons, ILogger<FileWidgetViewModel> logger)
    {
        _icons = icons;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _reloadTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            if (CurrentPath is { } path)
                _ = LoadAsync(path, [], rename: false);
        };
    }

    public ShellIcons Icons => _icons;

    /// <summary>창 핸들 (Windows 대화상자의 소유자).</summary>
    public IntPtr OwnerHandle { get; set; }

    public ObservableCollection<SidebarFolderViewModel> Folders { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private ObservableCollection<FileItemViewModel> _items = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoUpCommand), nameof(RefreshCommand))]
    private string? _currentPath;

    /// <summary>지금 폴더가 들어 있는 왼쪽 목록의 폴더 (없으면 null).</summary>
    [ObservableProperty] private SidebarFolderViewModel? _currentRoot;

    [ObservableProperty] private IReadOnlyList<BreadcrumbViewModel> _breadcrumbs = [];

    /// <summary>목록 가운데 안내 (빈 폴더, 열 수 없는 폴더). 정상이면 null.</summary>
    [ObservableProperty] private string? _message;

    /// <summary>방금 한 동작이 실패한 이유 (상태 표시줄). 몇 초 뒤 사라진다.</summary>
    [ObservableProperty] private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private int _selectedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameSortGlyph), nameof(ExtensionSortGlyph))]
    private FileSortColumn _sortBy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameSortGlyph), nameof(ExtensionSortGlyph))]
    private bool _sortDescending;

    /// <summary>정렬 중인 열 머리글의 화살표 (오름차순 ˄, 내림차순 ˅).</summary>
    public string NameSortGlyph => SortBy == FileSortColumn.Name ? SortGlyph : "";

    public string ExtensionSortGlyph => SortBy == FileSortColumn.Extension ? SortGlyph : "";

    private string SortGlyph => SortDescending ? "" : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExtensionColumn))]
    private double _extensionColumnWidth = Core.Presets.FileWidgetLayout.DefaultExtensionColumnWidth;

    /// <summary>목록 너비 (창이 좁으면 확장자 열을 줄여 이름이 보이게).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExtensionColumn))]
    private double _listWidth;

    /// <summary>확장자 열: 사용자가 정한 너비, 단 목록의 35%를 넘지 않게.</summary>
    public GridLength ExtensionColumn =>
        new(ListWidth > 0 ? Math.Min(ExtensionColumnWidth, Math.Max(40, ListWidth * 0.35)) : ExtensionColumnWidth);

    public string StatusText
    {
        get
        {
            var count = Items.Count == 1 ? "1 item" : $"{Items.Count:N0} items";
            return SelectedCount switch
            {
                0 => count,
                1 => count + "    1 item selected",
                _ => count + $"    {SelectedCount:N0} items selected",
            };
        }
    }

    /// <summary>현재 폴더 · 정렬 · 열 너비가 바뀌었을 때 (설정에 저장).</summary>
    public event EventHandler? StateChanged;

    /// <summary>목록을 다시 읽은 뒤 (UI 스레드).</summary>
    public event EventHandler<ItemsLoadedEventArgs>? ItemsLoaded;

    /// <summary>
    /// 왼쪽 목록을 바꾼다. 아직 연 폴더가 없거나, 지금 보고 있던 폴더를 목록에서 뺐으면 첫 폴더를 연다.
    /// <paramref name="preferred"/>가 지금 폴더와 다르면(프리셋 적용 등) 그 폴더를 연다.
    /// </summary>
    public void SetFolders(IReadOnlyList<string> folders, string? preferred)
    {
        var changed = !Folders.Select(f => f.Path).SequenceEqual(folders, StringComparer.OrdinalIgnoreCase);
        var oldRoot = CurrentPath is null ? null : FindRoot(CurrentPath)?.Path;
        if (changed)
        {
            Folders.Clear();
            foreach (var folder in folders)
                Folders.Add(new SidebarFolderViewModel(folder, _icons));
        }
        // UI 스레드라서 네트워크 폴더는 있는지 묻지 않는다 (없으면 목록 자리에 안내가 뜬다)
        var first = folders.FirstOrDefault(FolderReader.ProbablyExists) ?? (folders.Count > 0 ? folders[0] : null);
        // 목록 밖(위로 가기로 올라간 곳)을 보고 있었으면 폴더를 더하거나 빼도 그대로 둔다
        var rootRemoved = oldRoot is not null && !folders.Any(f => FileTransferRules.PathsEqual(f, oldRoot));
        var preferredExists = preferred is not null && FolderReader.ProbablyExists(preferred);

        if (preferredExists && !FileTransferRules.PathsEqual(preferred, CurrentPath))
            Navigate(preferred);
        else if (CurrentPath is null)
            Navigate(preferredExists ? preferred : first);
        else if (rootRemoved && FindRoot(CurrentPath) is null)
            Navigate(first);
        else if (changed)
            UpdateLocation();
    }

    /// <summary>창이 보일 때만 폴더 변경을 감시한다.</summary>
    public void SetActive(bool active)
    {
        if (_active == active)
            return;
        _active = active;
        if (!active)
            Watch(null);
        else if (CurrentPath is { } path)
            _ = LoadAsync(path, [], rename: false); // 숨겨진 동안 바뀐 것 반영 (읽은 뒤 감시 시작)
    }

    /// <summary>설정에 저장된 정렬·열 너비 (다시 읽지 않아도 되면 그대로).</summary>
    public void ApplyView(FileSortColumn sortBy, bool descending, double extensionWidth)
    {
        ExtensionColumnWidth = Math.Clamp(extensionWidth, 40, 400);
        if (SortBy == sortBy && SortDescending == descending)
            return;
        SortBy = sortBy;
        SortDescending = descending;
        if (CurrentPath is { } path)
            _ = LoadAsync(path, [], rename: false);
    }

    /// <summary>다른 폴더로 (뒤로 가기 기록에 남긴다).</summary>
    /// <param name="select">연 뒤 선택할 항목 (예: 위로 갔을 때 방금 있던 폴더).</param>
    public void Navigate(string? path, string? select = null)
    {
        if (path is null)
            return;
        try
        {
            path = FileTransferRules.Normalize(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ShowError($"'{path}' is not a valid folder path."); // 직접 고친 설정 파일·프리셋 등
            return;
        }
        if (CurrentPath is not null && !FileTransferRules.PathsEqual(CurrentPath, path))
        {
            _back.Push(CurrentPath);
            _forward.Clear();
        }
        Go(path, select);
    }

    private void Go(string path, string? select)
    {
        CurrentPath = path;
        UpdateLocation();
        Watch(null); // 새 폴더는 읽고 나서 감시 (없는 폴더·끊긴 네트워크 폴더에 UI 스레드가 매달리지 않게)
        Error = null;
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        StateChanged?.Invoke(this, EventArgs.Empty);
        _ = LoadAsync(path, select is null ? [] : [select], rename: false);
    }

    private bool CanGoBack() => _back.Count > 0;

    private bool CanGoForward() => _forward.Count > 0;

    private bool CanGoUp() => CurrentPath is not null && Path.GetDirectoryName(CurrentPath) is not null;

    private bool HasFolder() => CurrentPath is not null;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    public void GoBack()
    {
        if (_back.Count == 0)
            return;
        var from = CurrentPath;
        if (from is not null)
            _forward.Push(from);
        var target = _back.Pop();
        Go(target, ChildToSelect(target, from));
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    public void GoForward()
    {
        if (_forward.Count == 0)
            return;
        var from = CurrentPath;
        if (from is not null)
            _back.Push(from);
        var target = _forward.Pop();
        Go(target, ChildToSelect(target, from));
    }

    /// <summary>상위 폴더로. 방금 있던 폴더를 선택해 둔다 (파일 탐색기처럼).</summary>
    [RelayCommand(CanExecute = nameof(CanGoUp))]
    public void GoUp()
    {
        if (CurrentPath is { } path && Path.GetDirectoryName(path) is { } parent)
            Navigate(parent, path);
    }

    [RelayCommand(CanExecute = nameof(HasFolder))]
    public void Refresh()
    {
        if (CurrentPath is { } path)
        {
            Error = null;
            _ = LoadAsync(path, [], rename: false);
        }
    }

    /// <summary>열 머리글을 누름: 같은 열이면 순서를 뒤집고, 다른 열이면 그 열로 오름차순.</summary>
    [RelayCommand]
    public void Sort(FileSortColumn column) =>
        SetSort(column, column == SortBy ? !SortDescending : false);

    public void SetSort(FileSortColumn column, bool descending)
    {
        if (column == SortBy && descending == SortDescending)
            return;
        SortBy = column;
        SortDescending = descending;
        StateChanged?.Invoke(this, EventArgs.Empty);
        if (CurrentPath is { } path)
            _ = LoadAsync(path, [], rename: false);
    }

    /// <summary>확장자 열 너비를 끌어서 바꾸는 중.</summary>
    public void ResizeExtensionColumn(double width) => ExtensionColumnWidth = Math.Clamp(width, 40, 400);

    /// <summary>확장자 열 너비 조절을 마침 (저장).</summary>
    public void CommitColumnWidth() => StateChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>파일을 만들거나 이름을 바꾼 직후: 감시 알림을 기다리지 않고 바로 다시 읽어서 그 항목을 선택한다.</summary>
    public Task ReloadAndSelectAsync(IReadOnlyList<string> select, bool rename = false) =>
        CurrentPath is { } path ? LoadAsync(path, select, rename) : Task.CompletedTask;

    public void ShowError(string message)
    {
        Error = message;
        var shown = message;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (Error == shown)
                Error = null;
        };
        timer.Start();
    }

    /// <summary>
    /// 폴더 내용을 백그라운드에서 읽어 목록에 반영. 같은 폴더·같은 정렬이면 바뀐 줄만 넣고 빼서
    /// 선택과 스크롤 위치가 그대로 남는다.
    /// </summary>
    private async Task LoadAsync(string path, IReadOnlyList<string> select, bool rename)
    {
        var version = ++_loadVersion;
        var sort = (SortBy, SortDescending);
        var (showHidden, showProtected, _) = ShellFiles.GetExplorerSettings();
        List<FileEntry>? entries = null;
        string? message = null;
        try
        {
            entries = await Task.Run(() => FolderReader.Sort(FolderReader.List(path, showHidden, showProtected), sort.SortBy, sort.SortDescending));
        }
        catch (UnauthorizedAccessException)
        {
            message = "You don't have permission to open this folder.";
        }
        catch (DirectoryNotFoundException)
        {
            message = "This folder can't be found. It may have been moved, renamed or deleted, or it's on a drive that isn't connected.";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            message = ex.Message;
        }
        if (version != _loadVersion)
            return; // 그사이 다른 폴더로 감

        var sameFolder = FileTransferRules.PathsEqual(path, _loadedPath);
        if (entries is null)
        {
            Items = [];
            Message = message;
        }
        else if (sameFolder && _loadedSort == sort && Items.Count > 0)
        {
            Sync(entries);
            Message = entries.Count == 0 ? "This folder is empty." : null;
        }
        else
        {
            // 정렬만 바뀌었으면 아이콘을 다시 읽지 않도록 줄을 재사용
            var old = sameFolder ? Items.ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase) : null;
            Items = new ObservableCollection<FileItemViewModel>(entries.Select(e =>
                old is not null && old.TryGetValue(e.FullPath, out var vm) && vm.IsFolder == e.IsFolder ? vm : new FileItemViewModel(e, _icons)));
            Message = entries.Count == 0 ? "This folder is empty." : null;
        }
        _loadedPath = path;
        _loadedSort = sort;
        // 읽을 수 있는 폴더만 감시한다. 이미 감시 중이면 그대로 (다시 읽을 때마다 새로 만들지 않게).
        if (entries is not null && _active && !FileTransferRules.PathsEqual(_watcher?.Path, path))
            Watch(path);
        OnPropertyChanged(nameof(StatusText));
        ItemsLoaded?.Invoke(this, new ItemsLoadedEventArgs(select, rename, sameFolder));
    }

    /// <summary>
    /// 지금 목록을 새 내용에 맞춘다: 없어진 줄은 빼고, 새 줄은 제자리에 넣는다.
    /// 둘 다 같은 순서로 정렬되어 있으므로 남은 줄의 순서는 그대로다.
    /// </summary>
    private void Sync(List<FileEntry> entries)
    {
        var fresh = entries.ToDictionary(e => e.FullPath, StringComparer.OrdinalIgnoreCase);
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            var item = Items[i];
            if (!fresh.TryGetValue(item.Path, out var entry) || entry.IsFolder != item.IsFolder)
                Items.RemoveAt(i);
            else
                item.Update(entry);
        }
        var existing = new HashSet<string>(Items.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (i < Items.Count && string.Equals(Items[i].Path, entry.FullPath, StringComparison.OrdinalIgnoreCase))
                continue;
            if (existing.Contains(entry.FullPath))
            {
                // 이론상 오지 않는다 (정렬 기준이 같으면 순서가 같다). 오면 그 줄을 제자리로 옮긴다.
                var from = IndexOf(entry.FullPath, i);
                Items.Move(from, i);
            }
            else
            {
                Items.Insert(i, new FileItemViewModel(entry, _icons));
            }
        }
        while (Items.Count > entries.Count)
            Items.RemoveAt(Items.Count - 1);
    }

    private int IndexOf(string path, int start)
    {
        for (var i = start; i < Items.Count; i++)
        {
            if (string.Equals(Items[i].Path, path, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return Items.Count - 1;
    }

    /// <summary>주소 표시줄과 왼쪽 목록의 선택을 지금 폴더에 맞춘다.</summary>
    private void UpdateLocation()
    {
        var path = CurrentPath;
        CurrentRoot = path is null ? null : FindRoot(path);
        GoUpCommand.NotifyCanExecuteChanged();
        if (path is null)
        {
            Breadcrumbs = [];
            return;
        }

        var crumbs = new List<BreadcrumbViewModel>();
        string start;
        if (CurrentRoot is { } root)
        {
            // 고른 폴더 안이면 그 폴더 이름부터 ("문서 › 프로젝트 › moniMS")
            start = FileTransferRules.Normalize(root.Path);
            crumbs.Add(new BreadcrumbViewModel(root.Name, start, true));
        }
        else
        {
            start = Path.GetPathRoot(path) ?? path;
            crumbs.Add(new BreadcrumbViewModel(ShellIcons.GetDisplayName(start), start, true));
        }
        var rest = path.Length > start.Length ? path[start.Length..].Trim(Path.DirectorySeparatorChar) : "";
        var current = start;
        foreach (var part in rest.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            crumbs.Add(new BreadcrumbViewModel(part, current, false));
        }
        Breadcrumbs = crumbs;
    }

    /// <summary>경로가 들어 있는 왼쪽 목록의 폴더 중 가장 깊은 것.</summary>
    private SidebarFolderViewModel? FindRoot(string path) =>
        Folders.Where(f => FileTransferRules.IsInside(path, f.Path)).MaxBy(f => f.Path.Length);

    /// <summary>뒤로/앞으로 갔을 때, 방금 있던 폴더가 도착한 폴더 바로 안에 있으면 그것을 선택.</summary>
    private static string? ChildToSelect(string target, string? from) =>
        from is not null && FileTransferRules.PathsEqual(Path.GetDirectoryName(from), target) ? from : null;

    private void Watch(string? path)
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
        if (path is null)
            return; // 감시는 폴더를 읽은 뒤에만 시작하므로 여기서 다시 있는지 묻지 않는다
        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            };
            watcher.Created += OnFolderChanged;
            watcher.Deleted += OnFolderChanged;
            watcher.Renamed += OnFolderChanged;
            watcher.Error += (_, e) =>
            {
                _logger.LogDebug(e.GetException(), "File widget watcher error");
                OnFolderChanged(this, EventArgs.Empty); // 알림이 넘쳤거나 폴더가 사라짐 → 다시 읽어서 확인
            };
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Can't watch {Path}", path); // 감시가 안 되는 드라이브: F5로 새로 고침
        }
    }

    /// <summary>감시 스레드에서 온다.</summary>
    private void OnFolderChanged(object? sender, EventArgs e) =>
        _dispatcher.BeginInvoke(() =>
        {
            _reloadTimer.Stop();
            _reloadTimer.Start();
        });

    public void Dispose()
    {
        _reloadTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
    }
}
