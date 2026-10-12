using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoniMS.App.Interop;
using MoniMS.App.Services;
using MoniMS.Core.Presets;

namespace MoniMS.App.ViewModels;

/// <summary>설정 창 "File widget" 탭: 보여 줄 폴더 고르기, 표시, 이동·크기 조절, 스타일·불투명도.</summary>
public sealed partial class FileWidgetSettingsViewModel : ObservableObject
{
    private readonly FileWidgetController _widget;
    private readonly ShellIcons _icons;
    private bool _loading;

    public FileWidgetSettingsViewModel(FileWidgetController widget, ShellIcons icons)
    {
        _widget = widget;
        _icons = icons;
        Load();
        _widget.LayoutChanged += OnLayoutChanged;
    }

    /// <summary>창이 닫힐 때 호출 (이벤트 구독 해제).</summary>
    public void Detach() => _widget.LayoutChanged -= OnLayoutChanged;

    public ObservableCollection<SidebarFolderViewModel> Folders { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveFolderCommand), nameof(MoveFolderUpCommand), nameof(MoveFolderDownCommand))]
    private SidebarFolderViewModel? _selectedFolder;

    [ObservableProperty] private bool _hasFolders;
    [ObservableProperty] private bool _isShown;
    [ObservableProperty] private bool _editMode;
    [ObservableProperty] private WidgetStyle _style;
    [ObservableProperty] private double _widgetOpacity;

    public IReadOnlyList<WidgetStyle> Styles { get; } = Enum.GetValues<WidgetStyle>();

    private void OnLayoutChanged(object? sender, EventArgs e) => Load();

    private void Load()
    {
        _loading = true;
        var l = _widget.Layout;
        var selected = SelectedFolder?.Path;
        if (!Folders.Select(f => f.Path).SequenceEqual(l.Folders, StringComparer.OrdinalIgnoreCase))
        {
            Folders.Clear();
            foreach (var folder in l.Folders)
                Folders.Add(new SidebarFolderViewModel(folder, _icons));
            SelectedFolder = Folders.FirstOrDefault(f => string.Equals(f.Path, selected, StringComparison.OrdinalIgnoreCase));
        }
        HasFolders = l.HasFolders;
        IsShown = l.Visible;
        EditMode = _widget.IsEditMode;
        Style = l.Style;
        WidgetOpacity = l.Opacity;
        _loading = false;
        MoveFolderUpCommand.NotifyCanExecuteChanged();
        MoveFolderDownCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsShownChanged(bool value)
    {
        if (!_loading)
            _widget.Update(l => l.Visible = value);
    }

    partial void OnEditModeChanged(bool value)
    {
        if (!_loading)
            _widget.SetEditMode(value);
    }

    partial void OnStyleChanged(WidgetStyle value)
    {
        if (!_loading)
            _widget.Update(l => l.Style = value);
    }

    partial void OnWidgetOpacityChanged(double value)
    {
        if (!_loading)
            _widget.Update(l => l.Opacity = Math.Clamp(value, 0, 1));
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose folders to show in the file widget", Multiselect = true };
        if (dialog.ShowDialog() != true)
            return;
        _widget.AddFolders(dialog.FolderNames);
        SelectedFolder = Folders.LastOrDefault(f => dialog.FolderNames.Any(n =>
            Core.Files.FileTransferRules.PathsEqual(n, f.Path)));
    }

    private bool HasSelection() => SelectedFolder is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RemoveFolder()
    {
        if (SelectedFolder is { } folder)
            _widget.RemoveFolder(folder.Path);
    }

    private bool CanMoveUp() => SelectedFolder is not null && Folders.IndexOf(SelectedFolder) > 0;

    private bool CanMoveDown() => SelectedFolder is not null && Folders.IndexOf(SelectedFolder) < Folders.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveFolderUp() => MoveFolder(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveFolderDown() => MoveFolder(+1);

    private void MoveFolder(int delta)
    {
        if (SelectedFolder is not { } folder)
            return;
        var i = Folders.IndexOf(folder);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= Folders.Count)
            return;
        _widget.Update(l =>
        {
            var path = l.Folders[i];
            l.Folders.RemoveAt(i);
            l.Folders.Insert(j, path);
        });
        SelectedFolder = Folders[j];
    }
}
