using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MoniMS.App.Interop;
using MoniMS.App.Services;
using MoniMS.App.ViewModels;
using MoniMS.Core.Files;
using MoniMS.Core.Presets;

namespace MoniMS.App.Views;

/// <summary>
/// 파일 위젯 창: 파일 탐색기의 "자세히" 보기를 줄인 모양. 파일을 열고, 끌어 놓고, 복사·잘라내기·붙여넣기,
/// 이름 바꾸기, 삭제(휴지통), 우클릭 메뉴(+ Windows의 원래 메뉴)를 쓸 수 있다.
/// 편집 모드에서는 안을 끌면 이동, 가장자리를 끌면 크기 조절 (사진 위젯과 같은 방식, 비율은 자유).
/// </summary>
public partial class FileWidgetWindow : Window
{
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_CAPTURECHANGED = 0x0215;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    private const int WMSZ_LEFT = 1, WMSZ_RIGHT = 2, WMSZ_TOP = 3, WMSZ_TOPLEFT = 4, WMSZ_TOPRIGHT = 5, WMSZ_BOTTOM = 6, WMSZ_BOTTOMLEFT = 7, WMSZ_BOTTOMRIGHT = 8;
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    /// <summary>크기 조절을 잡는 가장자리 폭 / 오른쪽 아래 모서리 크기 (DIP).</summary>
    private const double EdgeSize = 10;
    private const double CornerSize = 22;

    /// <summary>이보다 많이 한꺼번에 열려고 하면 확인한다 (파일 탐색기와 같은 기준).</summary>
    private const int ConfirmOpenCount = 15;

    /// <summary>우클릭 메뉴에 옮겨 줄 색 (메뉴는 창 밖의 팝업이라 창의 리소스를 찾지 못한다).</summary>
    private static readonly string[] MenuBrushKeys = ["FwText", "FwSubtle", "FwMenuBackground", "FwMenuBorder", "FwMenuHover", "FwAccent"];

    private readonly FileWidgetViewModel _vm;
    private readonly FileWidgetController _controller;
    private bool _editMode;

    private int _resizeEdge;
    private POINT _resizeStartCursor;
    private RECT _resizeStartRect;

    /// <summary>끌기 시작점 (항목 위에서 왼쪽 버튼을 누른 곳).</summary>
    private Point? _dragStart;
    private FileItemViewModel? _pressedItem;

    /// <summary>여러 개 선택된 상태에서 그중 하나를 눌렀을 때: 끌 수 있도록 선택 변경을 버튼을 뗄 때로 미룬다.</summary>
    private bool _selectOnRelease;

    private object? _dropHighlight;
    private bool _renameBusy;

    public FileWidgetWindow(FileWidgetViewModel vm, FileWidgetController controller)
    {
        InitializeComponent();
        _vm = vm;
        _controller = controller;
        DataContext = vm;
        // 우클릭 메뉴는 열 때마다 새로 만든다. 처음부터 메뉴가 붙어 있어야 WPF가 이 목록에 메뉴를 띄워 준다.
        FileList.ContextMenu = NewMenu();
        SidebarList.ContextMenu = NewMenu();
        SourceInitialized += (_, _) => HwndSource.FromHwnd(Handle)?.AddHook(WndProc);
        vm.ItemsLoaded += OnItemsLoaded;
        Closed += (_, _) => vm.ItemsLoaded -= OnItemsLoaded;
        // 주소가 길면 지금 폴더 쪽(오른쪽 끝)이 보이게
        CrumbScroller.ScrollChanged += (_, e) =>
        {
            if (e.ExtentWidthChange != 0 || e.ViewportWidthChange != 0)
                CrumbScroller.ScrollToRightEnd();
        };
    }

    /// <summary>이동이나 크기 조절을 마쳤을 때 (편집 모드).</summary>
    public event EventHandler? MovedOrResized;

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    // ================= 모양 =================

    /// <param name="backgroundOpacity">배경만의 불투명도. 글자·아이콘은 그대로.</param>
    public void ApplyStyle(WidgetStyle style, Color accent, double backgroundOpacity)
    {
        var light = style == WidgetStyle.Light;
        // 배경/테두리는 "불투명도 100%일 때"의 색. 유리 스타일은 100%여도 반투명.
        var (bg, border, text, subtle, divider, hover, input) = style switch
        {
            WidgetStyle.Light => ("#FFF9FAFB", "#1F000000", "#FF1A1A1A", "#FF5F6672", "#14000000", "#0C000000", "#0A000000"),
            WidgetStyle.Glass => ("#66000000", "#40FFFFFF", "#FFFFFFFF", "#FFD6DCE5", "#26FFFFFF", "#1AFFFFFF", "#1AFFFFFF"),
            _ => ("#FF1F2023", "#33FFFFFF", "#FFF3F4F6", "#FF9AA1AC", "#1AFFFFFF", "#12FFFFFF", "#12FFFFFF"),
        };
        var opacity = Math.Clamp(backgroundOpacity, 0, 1);
        var background = ScaleAlpha(Parse(bg), opacity);
        // 완전히 투명한 픽셀은 클릭이 바탕화면으로 빠져나가므로 아주 조금은 남긴다 (눈에는 안 보임)
        if (background.A == 0)
            background.A = 1;

        SetBrush("FwBackground", background);
        SetBrush("FwBorder", ScaleAlpha(Parse(border), Math.Max(opacity, 0.3)));
        SetBrush("FwText", Parse(text));
        SetBrush("FwSubtle", Parse(subtle));
        SetBrush("FwDivider", Parse(divider));
        SetBrush("FwHover", Parse(hover));
        SetBrush("FwInput", Parse(input));
        SetBrush("FwSelected", Color.FromArgb(light ? (byte)0x38 : (byte)0x55, accent.R, accent.G, accent.B));
        SetBrush("FwSelectedHover", Color.FromArgb(light ? (byte)0x4C : (byte)0x70, accent.R, accent.G, accent.B));
        SetBrush("FwAccent", accent);
        SetBrush("FwError", light ? Parse("#FFC42B1C") : Parse("#FFFF99A4"));
        // 메뉴·이름 입력 상자는 배경이 비치면 읽기 어려우므로 항상 불투명
        SetBrush("FwMenuBackground", light ? Parse("#FFF9F9F9") : Parse("#FF2B2B2B"));
        SetBrush("FwMenuBorder", light ? Parse("#FFE0E0E0") : Parse("#FF3D3D3D"));
        SetBrush("FwMenuHover", light ? Parse("#0F000000") : Parse("#14FFFFFF"));
        // 앱 공용 스크롤바 색
        SetBrush("StrongStrokeBrush", light ? Parse("#40000000") : Parse("#50FFFFFF"));
        SetBrush("SubtleTextBrush", Parse(subtle));
        SetBrush("AccentBrush", accent);
    }

    public void SetEditMode(bool enabled)
    {
        if (!enabled)
            EndResize();
        _editMode = enabled;
        EditOverlay.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Color ScaleAlpha(Color c, double factor) => Color.FromArgb((byte)Math.Round(c.A * factor), c.R, c.G, c.B);

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private void SetBrush(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Resources[key] = brush;
    }

    // ================= 목록 =================

    private void OnItemsLoaded(object? sender, ItemsLoadedEventArgs e)
    {
        if (e.Select.Count > 0)
        {
            var wanted = new HashSet<string>(e.Select, StringComparer.OrdinalIgnoreCase);
            FileList.SelectedItems.Clear();
            FileItemViewModel? first = null;
            foreach (var item in _vm.Items.Where(i => wanted.Contains(i.Path)))
            {
                FileList.SelectedItems.Add(item);
                first ??= item;
            }
            if (first is not null)
            {
                FileList.ScrollIntoView(first);
                if (e.Rename)
                    BeginRename(first);
                else
                    FocusItem(first);
            }
        }
        else if (!e.SameFolder)
        {
            // 다른 폴더: 맨 위부터
            if (_vm.Items.Count > 0)
                FileList.ScrollIntoView(_vm.Items[0]);
        }
        _vm.SelectedCount = FileList.SelectedItems.Count;
    }

    private void OnFileSelectionChanged(object sender, SelectionChangedEventArgs e) => _vm.SelectedCount = FileList.SelectedItems.Count;

    private void OnFileListSizeChanged(object sender, SizeChangedEventArgs e) => _vm.ListWidth = e.NewSize.Width;

    /// <summary>선택한 항목 (목록 순서).</summary>
    private List<FileItemViewModel> SelectedItems()
    {
        var selected = new HashSet<object>(FileList.SelectedItems.Cast<object>());
        return _vm.Items.Where(selected.Contains).ToList();
    }

    /// <summary>창이 활성화돼 있을 때만 키보드 초점을 옮긴다 (자동 새로 고침이 다른 앱의 초점을 뺏지 않게).</summary>
    private void FocusItem(FileItemViewModel item)
    {
        if (!IsActive)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            (FileList.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem)?.Focus());
    }

    private void OnSidebarItemClick(object sender, MouseButtonEventArgs e)
    {
        if (_editMode || sender is not ListBoxItem { DataContext: SidebarFolderViewModel folder })
            return;
        if (!FileTransferRules.PathsEqual(folder.Path, _vm.CurrentPath))
            _vm.Navigate(folder.Path);
    }

    /// <summary>키보드(화살표)로 왼쪽 목록을 옮겨 다닐 때.</summary>
    private void OnSidebarSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SidebarList.IsKeyboardFocusWithin && Mouse.LeftButton == MouseButtonState.Released &&
            SidebarList.SelectedItem is SidebarFolderViewModel folder && !ReferenceEquals(folder, _vm.CurrentRoot))
            _vm.Navigate(folder.Path);
    }

    private void OnCrumbClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BreadcrumbViewModel crumb })
            _vm.Navigate(crumb.Path);
    }

    private void OnAddFolderClick(object sender, RoutedEventArgs e) => AddFolders();

    private void AddFolders()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose folders to show in the file widget", Multiselect = true };
        if (dialog.ShowDialog(this) == true)
            _controller.AddFolders(dialog.FolderNames);
    }

    private void OnColumnGripDrag(object sender, DragDeltaEventArgs e) =>
        _vm.ResizeExtensionColumn(_vm.ExtensionColumn.Value - e.HorizontalChange);

    private void OnColumnGripDone(object sender, DragCompletedEventArgs e) => _vm.CommitColumnWidth();

    // ================= 마우스: 열기 · 선택 · 끌기 =================

    private void OnFilePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        _selectOnRelease = false;
        if (_editMode || IsInside<TextBox>(e.OriginalSource) || IsInside<ScrollBar>(e.OriginalSource))
            return;
        if (ContainerAt(e.OriginalSource) is not { DataContext: FileItemViewModel item } container)
            return;

        if (e.ClickCount == 2)
        {
            e.Handled = true;
            OpenItems(container.IsSelected ? SelectedItems() : [item]);
            return;
        }
        _dragStart = e.GetPosition(FileList);
        _pressedItem = item;
        if (container.IsSelected && FileList.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            _selectOnRelease = true;
            e.Handled = true;
            container.Focus();
        }
    }

    private void OnFilePreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || _pressedItem is not { } pressed || e.LeftButton != MouseButtonState.Pressed)
            return;
        var delta = e.GetPosition(FileList) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _dragStart = null;
        _selectOnRelease = false;
        var items = FileList.SelectedItems.Contains(pressed) ? SelectedItems() : [pressed];
        // 다른 앱(파일 탐색기, 메신저, 브라우저 …)에 끌어 놓을 수 있다. 이동은 받는 쪽이 한다.
        var data = new DataObject(DataFormats.FileDrop, items.Select(i => i.Path).ToArray());
        try
        {
            DragDrop.DoDragDrop(FileList, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        catch (COMException)
        {
            // 받는 쪽 오류
        }
        SetDropHighlight(null);
    }

    private void OnFilePreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_selectOnRelease && _pressedItem is { } item)
        {
            FileList.SelectedItems.Clear();
            FileList.SelectedItem = item;
        }
        _selectOnRelease = false;
        _dragStart = null;
    }

    /// <summary>빈 곳을 누르면 선택 해제.</summary>
    private void OnFileListBackgroundDown(object sender, MouseButtonEventArgs e)
    {
        if (_editMode || ContainerAt(e.OriginalSource) is not null)
            return;
        FileList.UnselectAll();
        FileList.Focus();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_editMode)
            DragMove(); // 끝나면 WM_EXITSIZEMOVE → MovedOrResized
    }

    /// <summary>마우스 앞으로/뒤로 버튼.</summary>
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (_editMode)
            return;
        if (e.ChangedButton == MouseButton.XButton1)
            _vm.GoBack();
        else if (e.ChangedButton == MouseButton.XButton2)
            _vm.GoForward();
    }

    // ================= 끌어 놓기 (받는 쪽) =================

    /// <summary>놓을 폴더: 폴더 줄 위면 그 폴더, 왼쪽 목록이면 그 폴더, 아니면 지금 폴더.</summary>
    private (string Folder, object? Highlight)? DropTargetAt(object sender, DragEventArgs e)
    {
        if (ReferenceEquals(sender, SidebarList))
        {
            return ItemsControl.ContainerFromElement(SidebarList, e.OriginalSource as DependencyObject) is ListBoxItem
            {
                DataContext: SidebarFolderViewModel { Exists: true } folder,
            }
                ? (folder.Path, folder)
                : null;
        }
        if (ContainerAt(e.OriginalSource)?.DataContext is FileItemViewModel { IsFolder: true } target)
            return (target.Path, target);
        return _vm.CurrentPath is { } current && _vm.Message is null ? (current, null) : null;
    }

    private TransferAction DropAction(object sender, DragEventArgs e, out (string Folder, object? Highlight)? target)
    {
        target = null;
        if (_editMode || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
            return TransferAction.None;
        target = DropTargetAt(sender, e);
        if (target is not { } t)
            return TransferAction.None;
        var action = FileTransferRules.ForDrop(paths, t.Folder,
            e.KeyStates.HasFlag(DragDropKeyStates.ControlKey), e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey));
        // 보내는 쪽이 허락한 동작만
        if (action == TransferAction.Move && !e.AllowedEffects.HasFlag(DragDropEffects.Move))
            action = e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? TransferAction.Copy : TransferAction.None;
        if (action == TransferAction.Copy && !e.AllowedEffects.HasFlag(DragDropEffects.Copy))
            action = TransferAction.None;
        return action;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var action = DropAction(sender, e, out var target);
        SetDropHighlight(action == TransferAction.None ? null : target?.Highlight);
        e.Effects = action switch
        {
            TransferAction.Copy => DragDropEffects.Copy,
            TransferAction.Move => DragDropEffects.Move,
            _ => DragDropEffects.None,
        };
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => SetDropHighlight(null);

    private void OnDrop(object sender, DragEventArgs e)
    {
        var action = DropAction(sender, e, out var target);
        SetDropHighlight(null);
        e.Handled = true;
        if (action == TransferAction.None || target is not { } t || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        // 복사·이동은 이쪽(Windows 파일 작업)이 한다. 보내는 쪽이 원본을 따로 지우지 않도록 "복사"로 알린다.
        e.Effects = DragDropEffects.Copy;
        ShellFiles.Transfer(paths, t.Folder, action, Handle, _ => { }, OnShellError);
    }

    private void SetDropHighlight(object? target)
    {
        if (ReferenceEquals(target, _dropHighlight))
            return;
        SetDropTarget(_dropHighlight, false);
        SetDropTarget(target, true);
        _dropHighlight = target;
    }

    private static void SetDropTarget(object? item, bool value)
    {
        if (item is FileItemViewModel file)
            file.IsDropTarget = value;
        else if (item is SidebarFolderViewModel folder)
            folder.IsDropTarget = value;
    }

    // ================= 키보드 =================

    /// <summary>폴더 이동 단축키는 목록보다 먼저 받는다 (Alt+←/→는 목록을 거치면 KeyDown까지 오지 않는다).</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || _editMode || e.OriginalSource is TextBox)
            return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var alt = Keyboard.Modifiers == ModifierKeys.Alt;
        e.Handled = true;
        switch (key)
        {
            case Key.Left when alt:
            case Key.BrowserBack:
                _vm.GoBack();
                break;
            case Key.Right when alt:
            case Key.BrowserForward:
                _vm.GoForward();
                break;
            case Key.Up when alt:
                _vm.GoUp();
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _editMode || e.OriginalSource is TextBox)
            return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        var inList = FileList.IsKeyboardFocusWithin;
        e.Handled = true;
        switch (key)
        {
            case Key.Back when mods == ModifierKeys.None:
                _vm.GoBack();
                break;
            case Key.F5:
            case Key.R when mods == ModifierKeys.Control:
                _vm.Refresh();
                break;
            case Key.N when mods == (ModifierKeys.Control | ModifierKeys.Shift):
                NewFolder();
                break;
            case Key.Enter when mods == ModifierKeys.Alt:
                ShowProperties(inList && SelectedItems() is { Count: > 0 } sel ? sel.Select(i => i.Path).ToList() : CurrentFolderAsList());
                break;
            case Key.Enter when inList && mods == ModifierKeys.None:
                OpenItems(SelectedItems());
                break;
            case Key.F2 when inList && SelectedItems() is [var only]:
                BeginRename(only);
                break;
            case Key.Delete when inList:
                DeleteItems(SelectedItems(), permanently: mods == ModifierKeys.Shift);
                break;
            case Key.C when inList && mods == ModifierKeys.Control:
                CopyItems(SelectedItems(), cut: false);
                break;
            case Key.X when inList && mods == ModifierKeys.Control:
                CopyItems(SelectedItems(), cut: true);
                break;
            case Key.C when inList && mods == (ModifierKeys.Control | ModifierKeys.Shift):
                CopyAsPath(SelectedItems().Select(i => i.Path));
                break;
            case Key.V when mods == ModifierKeys.Control:
                Paste(_vm.CurrentPath);
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    // ================= 우클릭 메뉴 =================

    /// <summary>
    /// 우클릭, 메뉴 키, Shift+F10 모두 여기로 온다 (WPF가 처리 순서를 맞춰 준다. 키를 뗄 때 메뉴가 닫히는 일이 없도록
    /// 직접 열지 않고 목록의 ContextMenu를 바꿔 끼운다). Shift를 누르고 있으면 Windows의 원래 메뉴.
    /// </summary>
    private void OnFileContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (IsInside<TextBox>(e.OriginalSource))
            return; // 이름 입력 상자는 자기 메뉴(잘라내기·붙여넣기)
        if (_editMode)
        {
            e.Handled = true;
            return;
        }
        var keyboard = IsKeyboardInvoked(e);
        var container = keyboard ? Keyboard.FocusedElement as ListBoxItem : ContainerAt(e.OriginalSource);
        var classic = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var point = MenuPoint(keyboard, container ?? (FrameworkElement)FileList);
        if (container?.DataContext is FileItemViewModel item)
        {
            if (!container.IsSelected)
            {
                FileList.SelectedItems.Clear();
                FileList.SelectedItem = item;
            }
            var items = SelectedItems();
            if (classic)
            {
                e.Handled = true;
                ShowClassicMenu(items, point);
                return;
            }
            FileList.ContextMenu = BuildItemMenu(items, point);
        }
        else
        {
            FileList.UnselectAll();
            if (classic)
            {
                e.Handled = true;
                ShowClassicFolderMenu(point);
                return;
            }
            FileList.ContextMenu = BuildBackgroundMenu(point);
        }
    }

    private void OnSidebarContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_editMode)
        {
            e.Handled = true;
            return;
        }
        var container = IsKeyboardInvoked(e)
            ? Keyboard.FocusedElement as ListBoxItem
            : ItemsControl.ContainerFromElement(SidebarList, e.OriginalSource as DependencyObject) as ListBoxItem;
        SidebarList.ContextMenu = container?.DataContext is SidebarFolderViewModel folder
            ? BuildSidebarMenu(folder)
            : BuildSidebarBackgroundMenu();
    }

    /// <summary>메뉴 키·Shift+F10으로 열면 WPF가 커서 위치를 -1로 준다.</summary>
    private static bool IsKeyboardInvoked(ContextMenuEventArgs e) => e.CursorLeft < 0 && e.CursorTop < 0;

    /// <summary>Windows 원래 메뉴를 띄울 화면 위치: 마우스면 커서, 키보드면 그 줄 아래.</summary>
    private static POINT MenuPoint(bool keyboard, FrameworkElement anchor)
    {
        if (!keyboard && GetCursorPos(out var cursor))
            return cursor;
        var origin = anchor.PointToScreen(new Point(24, Math.Min(anchor.ActualHeight, 26)));
        return new POINT { X = (int)origin.X, Y = (int)origin.Y };
    }

    private ContextMenu BuildItemMenu(List<FileItemViewModel> items, POINT cursor)
    {
        var menu = NewMenu();
        var single = items.Count == 1 ? items[0] : null;
        var paths = items.Select(i => i.Path).ToList();

        menu.Items.Add(Item("Open", "", () => OpenItems(items), "Enter", bold: true));
        if (single is { IsFolder: false })
            menu.Items.Add(Item("Open with…", "", () => ShellFiles.OpenWith(single.Path, Handle, OnShellError)));
        if (single is { IsFolder: true })
            menu.Items.Add(Item("Open in File Explorer", "", () => ShellFiles.OpenInExplorer(single.Path, OnShellError)));
        else if (single is not null)
            menu.Items.Add(Item("Show in File Explorer", "", () => ShellFiles.ShowInExplorer(single.Path, OnShellError)));
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("Cut", "", () => CopyItems(items, cut: true), "Ctrl+X"));
        menu.Items.Add(Item("Copy", "", () => CopyItems(items, cut: false), "Ctrl+C"));
        if (single is { IsFolder: true })
            menu.Items.Add(Item("Paste into folder", "", () => Paste(single.Path), enabled: ShellFiles.ClipboardHasFiles()));
        menu.Items.Add(Item("Copy as path", "", () => CopyAsPath(paths), "Ctrl+Shift+C"));
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("Rename", "", () => BeginRename(single!), "F2", enabled: single is not null));
        menu.Items.Add(Item("Delete", "", () => DeleteItems(items, permanently: false), "Del"));
        menu.Items.Add(new Separator());

        if (single is { IsFolder: true })
        {
            menu.Items.Add(_controller.ContainsFolder(single.Path)
                ? Item("Remove from sidebar", "", () => _controller.RemoveFolder(single.Path))
                : Item("Add to sidebar", "", () => _controller.AddFolders([single.Path])));
        }
        menu.Items.Add(Item("Properties", "", () => ShowProperties(paths), "Alt+Enter"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Show more options", "", () => ShowClassicMenu(items, cursor), "Shift+F10"));
        return menu;
    }

    private ContextMenu BuildBackgroundMenu(POINT cursor)
    {
        var menu = NewMenu();
        var hasFolder = _vm.CurrentPath is not null && _vm.Message is null;

        var sort = Item("Sort by", "", null);
        sort.Items.Add(Check("Name", _vm.SortBy == FileSortColumn.Name, () => _vm.SetSort(FileSortColumn.Name, _vm.SortDescending)));
        sort.Items.Add(Check("Extension", _vm.SortBy == FileSortColumn.Extension, () => _vm.SetSort(FileSortColumn.Extension, _vm.SortDescending)));
        sort.Items.Add(new Separator());
        sort.Items.Add(Check("Ascending", !_vm.SortDescending, () => _vm.SetSort(_vm.SortBy, false)));
        sort.Items.Add(Check("Descending", _vm.SortDescending, () => _vm.SetSort(_vm.SortBy, true)));
        menu.Items.Add(sort);
        menu.Items.Add(Item("Refresh", "", _vm.Refresh, "F5"));
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("Paste", "", () => Paste(_vm.CurrentPath), "Ctrl+V", enabled: hasFolder && ShellFiles.ClipboardHasFiles()));
        menu.Items.Add(Item("New folder", "", NewFolder, "Ctrl+Shift+N", enabled: hasFolder));
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("Open in File Explorer", "", () => ShellFiles.OpenInExplorer(_vm.CurrentPath!, OnShellError), enabled: hasFolder));
        menu.Items.Add(Item("Properties", "", () => ShowProperties(CurrentFolderAsList()), "Alt+Enter", enabled: hasFolder));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Show more options", "", () => ShowClassicFolderMenu(cursor), "Shift+F10", enabled: hasFolder));
        return menu;
    }

    private ContextMenu BuildSidebarMenu(SidebarFolderViewModel folder)
    {
        var menu = NewMenu();
        menu.Items.Add(Item("Open", "", () => _vm.Navigate(folder.Path), bold: true, enabled: folder.Exists));
        menu.Items.Add(Item("Open in File Explorer", "", () => ShellFiles.OpenInExplorer(folder.Path, OnShellError), enabled: folder.Exists));
        menu.Items.Add(Item("Paste into folder", "", () => Paste(folder.Path), enabled: folder.Exists && ShellFiles.ClipboardHasFiles()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Remove from sidebar", "", () => _controller.RemoveFolder(folder.Path)));
        menu.Items.Add(Item("Add folder…", "", AddFolders));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Properties", "", () => ShowProperties([folder.Path]), enabled: folder.Exists));
        return menu;
    }

    private ContextMenu BuildSidebarBackgroundMenu()
    {
        var menu = NewMenu();
        menu.Items.Add(Item("Add folder…", "", AddFolders));
        return menu;
    }

    private ContextMenu NewMenu()
    {
        var menu = new ContextMenu { Style = (Style)FindResource("FwContextMenu") };
        foreach (var key in MenuBrushKeys)
            menu.Resources[key] = Resources[key];
        menu.Resources[typeof(MenuItem)] = FindResource("FwMenuItem");
        menu.Resources[MenuItem.SeparatorStyleKey] = FindResource("FwMenuSeparator");
        return menu;
    }

    private MenuItem Item(string header, string icon, Action? action, string? gesture = null, bool enabled = true, bool bold = false)
    {
        var item = new MenuItem
        {
            Header = header,
            InputGestureText = gesture ?? "",
            IsEnabled = enabled,
            Icon = new TextBlock { Text = icon, FontFamily = (FontFamily)FindResource("FwIconFont"), FontSize = 13 },
        };
        if (bold)
            item.FontWeight = FontWeights.SemiBold;
        if (action is not null)
            item.Click += (_, _) => Run(action);
        return item;
    }

    private MenuItem Check(string header, bool isChecked, Action action)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked };
        item.Click += (_, _) => Run(action);
        return item;
    }

    /// <summary>Windows의 원래 우클릭 메뉴. 이름 바꾸기는 위젯 안에서 한다.</summary>
    private void ShowClassicMenu(List<FileItemViewModel> items, POINT at)
    {
        if (items.Count == 0)
            return;
        // WPF 메뉴가 완전히 닫힌 뒤에 연다
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Run(() =>
            ShellContextMenu.ShowForItems(Handle, items.Select(i => i.Path).ToList(), at.X, at.Y,
                Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), verb =>
                {
                    if (!string.Equals(verb, "rename", StringComparison.OrdinalIgnoreCase))
                        return false;
                    if (items is [var only])
                        BeginRename(only);
                    return true;
                })));
    }

    private void ShowClassicFolderMenu(POINT at)
    {
        if (_vm.CurrentPath is not { } folder || _vm.Message is not null)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Run(() =>
            ShellContextMenu.ShowForFolder(Handle, folder, at.X, at.Y, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))));
    }

    // ================= 동작 =================

    private void OpenItems(List<FileItemViewModel> items)
    {
        if (items.Count == 0)
            return;
        if (items is [{ IsFolder: true } folder])
        {
            _vm.Navigate(folder.Path);
            return;
        }
        if (items.Count > ConfirmOpenCount &&
            MessageBox.Show(this, $"Are you sure you want to open all {items.Count} items?", "MoniMS",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        foreach (var item in items)
        {
            if (item.IsFolder)
                ShellFiles.OpenInExplorer(item.Path, OnShellError);
            else
                ShellFiles.Open(item.Path, Handle, OnShellError);
        }
    }

    private void CopyItems(List<FileItemViewModel> items, bool cut)
    {
        if (items.Count == 0)
            return;
        try
        {
            ShellFiles.SetClipboard(items.Select(i => i.Path).ToList(), cut);
        }
        catch (ExternalException ex)
        {
            _vm.ShowError("Couldn't use the clipboard: " + ex.Message);
        }
    }

    private void CopyAsPath(IEnumerable<string> paths)
    {
        try
        {
            ShellFiles.CopyPaths(paths);
        }
        catch (ExternalException ex)
        {
            _vm.ShowError("Couldn't use the clipboard: " + ex.Message);
        }
    }

    private void Paste(string? targetFolder)
    {
        if (targetFolder is null || _vm.Message is not null && FileTransferRules.PathsEqual(targetFolder, _vm.CurrentPath))
            return;
        IReadOnlyList<string> paths;
        bool cut;
        try
        {
            (paths, cut) = ShellFiles.GetClipboard();
        }
        catch (ExternalException ex)
        {
            _vm.ShowError("Couldn't use the clipboard: " + ex.Message);
            return;
        }
        if (paths.Count == 0)
            return;
        var action = FileTransferRules.ForPaste(paths, targetFolder, cut);
        if (action == TransferAction.None)
        {
            if (FileTransferRules.IntoItself(paths, targetFolder))
                _vm.ShowError("The destination folder is inside the folder you're copying or moving.");
            return;
        }
        ShellFiles.Transfer(paths, targetFolder, action, Handle, ok =>
        {
            if (ok && cut)
                ShellFiles.ClearClipboardIfStill(paths); // 잘라내기는 한 번만 붙여 넣는다 (파일 탐색기처럼)
        }, OnShellError);
    }

    private void DeleteItems(List<FileItemViewModel> items, bool permanently)
    {
        if (items.Count > 0)
            ShellFiles.Delete(items.Select(i => i.Path).ToList(), permanently, Handle, _ => { }, OnShellError);
    }

    private void NewFolder()
    {
        if (_vm.CurrentPath is not { } folder || _vm.Message is not null)
            return;
        try
        {
            var path = Path.Combine(folder, FolderReader.UniqueName(folder, "New folder"));
            Directory.CreateDirectory(path);
            _ = _vm.ReloadAndSelectAsync([path], rename: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm.ShowError("Couldn't create a folder here: " + ex.Message);
        }
    }

    private void ShowProperties(List<string> paths)
    {
        if (paths.Count > 0)
            Run(() => ShellContextMenu.InvokeVerb(Handle, paths, "properties"));
    }

    private List<string> CurrentFolderAsList() => _vm.CurrentPath is { } p && _vm.Message is null ? [p] : [];

    /// <summary>메뉴·단축키 동작 실패를 상태 표시줄에 보여 준다 (위젯이 죽지 않게).</summary>
    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidOperationException
                                       or ArgumentException or Win32Exception)
        {
            OnShellError(ex);
        }
    }

    private void OnShellError(Exception ex)
    {
        if (ex is Win32Exception { NativeErrorCode: 1223 } || ex is COMException { HResult: unchecked((int)0x800704C7) })
            return; // 사용자가 취소함
        _vm.ShowError(ex.Message);
    }

    // ================= 이름 바꾸기 =================

    private void BeginRename(FileItemViewModel item)
    {
        foreach (var other in _vm.Items.Where(i => i.IsRenaming && !ReferenceEquals(i, item)))
            other.IsRenaming = false;
        FileList.SelectedItems.Clear();
        FileList.SelectedItem = item;
        FileList.ScrollIntoView(item);
        item.IsRenaming = true; // 입력 상자가 보이면 OnRenameBoxVisibleChanged에서 초점을 준다
    }

    private void OnRenameBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox { IsVisible: true } box || box.DataContext is not FileItemViewModel item)
            return;
        box.Text = item.Name;
        // 파일 탐색기처럼 확장자 앞까지만 선택
        var length = !item.IsFolder && item.Extension.Length > 0 && item.DisplayName.Length > 0
            ? item.Name.Length - item.Extension.Length - 1
            : item.Name.Length;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!box.IsVisible)
                return;
            Activate();
            box.Focus();
            box.Select(0, length);
        });
    }

    private void OnRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
            return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitRename(box, fromEnter: true);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (box.DataContext is FileItemViewModel item)
                EndRename(item);
        }
    }

    private void OnRenameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box)
            CommitRename(box, fromEnter: false);
    }

    /// <param name="fromEnter">Enter로 확정 (잘못된 이름이면 계속 고치게 한다). 아니면 다른 곳을 눌러 확정 (잘못된 이름이면 취소).</param>
    private void CommitRename(TextBox box, bool fromEnter)
    {
        if (_renameBusy || box.DataContext is not FileItemViewModel { IsRenaming: true } item)
            return;
        _renameBusy = true;
        try
        {
            var error = FolderReader.ValidateName(box.Text, out var name);
            if (error is null && name == item.Name)
            {
                EndRename(item);
                return;
            }
            if (error is not null)
            {
                _vm.ShowError(error);
                KeepEditing(box, item, fromEnter);
                return;
            }
            if (!item.IsFolder &&
                !string.Equals(Path.GetExtension(name), Path.GetExtension(item.Name), StringComparison.OrdinalIgnoreCase) &&
                MessageBox.Show(this, "If you change a file name extension, the file might become unusable.\n\nAre you sure you want to change it?",
                    "Rename", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                KeepEditing(box, item, fromEnter);
                return;
            }
            try
            {
                var renamed = ShellFiles.Rename(item.Path, name);
                EndRename(item);
                _ = _vm.ReloadAndSelectAsync([renamed]);
            }
            catch (IOException ex)
            {
                _vm.ShowError(ex.Message);
                KeepEditing(box, item, fromEnter);
            }
        }
        finally
        {
            _renameBusy = false;
        }
    }

    private void KeepEditing(TextBox box, FileItemViewModel item, bool fromEnter)
    {
        if (fromEnter)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => box.Focus());
        else
            EndRename(item);
    }

    private void EndRename(FileItemViewModel item)
    {
        item.IsRenaming = false;
        FocusItem(item);
    }

    // ================= 도우미 =================

    private ListBoxItem? ContainerAt(object source) =>
        ItemsControl.ContainerFromElement(FileList, source as DependencyObject) as ListBoxItem;

    private static bool IsInside<T>(object source) where T : DependencyObject
    {
        for (var d = source as DependencyObject; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is T)
                return true;
        }
        return false;
    }

    // ================= 편집 모드: 크기 조절 =================

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Windows 원래 우클릭 메뉴가 열려 있으면 하위 메뉴 그리기를 넘긴다
        if (ShellContextMenu.HandleMessage(msg, wParam, lParam, out var menuResult))
        {
            handled = true;
            return menuResult;
        }
        switch (msg)
        {
            case WM_NCHITTEST when _editMode:
                var hit = HitTestEdges(hwnd, lParam);
                if (hit != 0)
                {
                    handled = true; // Windows가 크기 조절 화살표 커서를 보여 준다
                    return hit;
                }
                break;
            case WM_NCLBUTTONDOWN when _editMode && wParam.ToInt32() is >= HTLEFT and <= HTBOTTOMRIGHT:
                BeginResize(hwnd, wParam.ToInt32() - (HTLEFT - WMSZ_LEFT));
                handled = true;
                break;
            case WM_MOUSEMOVE when _resizeEdge != 0:
                UpdateResize(hwnd);
                handled = true;
                break;
            case WM_LBUTTONUP when _resizeEdge != 0:
                EndResize();
                handled = true;
                break;
            case WM_CAPTURECHANGED when _resizeEdge != 0:
                EndResize(); // 다른 창이 마우스를 가져감 (Esc, 창 전환 등)
                break;
            case WM_EXITSIZEMOVE:
                MovedOrResized?.Invoke(this, EventArgs.Empty); // DragMove로 이동을 마침
                break;
        }
        return IntPtr.Zero;
    }

    private void BeginResize(IntPtr hwnd, int edge)
    {
        if (!GetCursorPos(out _resizeStartCursor) || !GetWindowRect(hwnd, out _resizeStartRect))
            return;
        _resizeEdge = edge;
        SetCapture(hwnd); // 버튼을 뗄 때까지 마우스 메시지를 이 창이 받는다
    }

    private void UpdateResize(IntPtr hwnd)
    {
        if (!GetCursorPos(out var cursor))
            return;
        var s = _resizeStartRect;
        var dpi = VisualTreeHelper.GetDpi(this);
        var r = ResizeRect((s.Left, s.Top, s.Right, s.Bottom), _resizeEdge,
            cursor.X - _resizeStartCursor.X, cursor.Y - _resizeStartCursor.Y,
            (int)Math.Ceiling(FileWidgetLayout.MinWidth * dpi.DpiScaleX), (int)Math.Ceiling(FileWidgetLayout.MinHeight * dpi.DpiScaleY));
        SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void EndResize()
    {
        if (_resizeEdge == 0)
            return;
        _resizeEdge = 0; // ReleaseCapture가 보내는 WM_CAPTURECHANGED에서 다시 들어오지 않도록 먼저
        ReleaseCapture();
        MovedOrResized?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>마우스가 가장자리에 있으면 그 방향의 크기 조절 위치(HT*)를 알려 준다.</summary>
    private int HitTestEdges(IntPtr hwnd, IntPtr lParam)
    {
        if (!GetWindowRect(hwnd, out var r))
            return 0;
        var p = lParam.ToInt64();
        int x = (short)(p & 0xFFFF), y = (short)((p >> 16) & 0xFFFF);
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var edge = EdgeSize * dpi;
        var corner = CornerSize * dpi;

        if (x >= r.Right - corner && y >= r.Bottom - corner)
            return HTBOTTOMRIGHT;
        bool left = x < r.Left + edge, right = x >= r.Right - edge, top = y < r.Top + edge, bottom = y >= r.Bottom - edge;
        return (top, bottom, left, right) switch
        {
            (true, _, true, _) => HTTOPLEFT,
            (true, _, _, true) => HTTOPRIGHT,
            (_, true, true, _) => HTBOTTOMLEFT,
            (_, true, _, true) => HTBOTTOMRIGHT,
            (true, _, _, _) => HTTOP,
            (_, true, _, _) => HTBOTTOM,
            (_, _, true, _) => HTLEFT,
            (_, _, _, true) => HTRIGHT,
            _ => 0,
        };
    }

    /// <summary>끄는 변만 움직이고 반대쪽 변은 제자리. 최소 크기보다 작아지지 않는다 (픽셀).</summary>
    private static (int Left, int Top, int Right, int Bottom) ResizeRect(
        (int Left, int Top, int Right, int Bottom) start, int edge, int dx, int dy, int minWidth, int minHeight)
    {
        bool left = edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT;
        bool right = edge is WMSZ_RIGHT or WMSZ_TOPRIGHT or WMSZ_BOTTOMRIGHT;
        bool top = edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT;
        bool bottom = edge is WMSZ_BOTTOM or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT;
        var (l, t, r, b) = start;
        if (left)
            l = Math.Min(start.Left + dx, start.Right - minWidth);
        if (right)
            r = Math.Max(start.Right + dx, start.Left + minWidth);
        if (top)
            t = Math.Min(start.Top + dy, start.Bottom - minHeight);
        if (bottom)
            b = Math.Max(start.Bottom + dy, start.Top + minHeight);
        return (l, t, r, b);
    }

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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SetCapture(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
