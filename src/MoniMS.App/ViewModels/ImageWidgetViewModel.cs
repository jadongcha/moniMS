using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoniMS.App.Imaging;
using MoniMS.App.Services;
using MoniMS.Core.Presets;

namespace MoniMS.App.ViewModels;

/// <summary>설정 창 "Image widget" 탭: 사진/GIF 고르기, 표시, 이동·크기 조절, 크기·불투명도·모서리.</summary>
public sealed partial class ImageWidgetViewModel : ObservableObject
{
    private readonly ImageWidgetController _widget;
    private bool _loading;
    private string? _thumbnailPath;

    public ImageWidgetViewModel(ImageWidgetController widget)
    {
        _widget = widget;
        Load();
        _widget.LayoutChanged += OnLayoutChanged;
    }

    /// <summary>창이 닫힐 때 호출 (이벤트 구독 해제).</summary>
    public void Detach() => _widget.LayoutChanged -= OnLayoutChanged;

    [ObservableProperty] private bool _hasImage;
    [ObservableProperty] private string _imageName = "";
    [ObservableProperty] private ImageSource? _thumbnail;
    [ObservableProperty] private string? _error;

    [ObservableProperty] private bool _isShown;
    [ObservableProperty] private bool _editMode;
    [ObservableProperty] private double _imageWidth;
    [ObservableProperty] private double _imageHeight;
    [ObservableProperty] private double _imageOpacity;
    [ObservableProperty] private double _cornerRadius;

    /// <summary>크기 슬라이더 최댓값 (화면 너비).</summary>
    public double MaxWidth { get; } = Math.Max(800, System.Windows.SystemParameters.VirtualScreenWidth);

    public string SizeText => $"{ImageWidth:0} × {ImageHeight:0}";

    private void OnLayoutChanged(object? sender, EventArgs e) => Load();

    private void Load()
    {
        _loading = true;
        var l = _widget.Layout;
        HasImage = l.HasImage;
        ImageName = l.ImageName ?? (l.ImagePath is null ? "" : Path.GetFileName(l.ImagePath));
        IsShown = l.Visible;
        EditMode = _widget.IsEditMode;
        ImageWidth = l.Width;
        ImageHeight = l.Height;
        ImageOpacity = l.Opacity;
        CornerRadius = l.CornerRadius;
        Error = _widget.Error;
        OnPropertyChanged(nameof(SizeText));
        if (!string.Equals(_thumbnailPath, l.ImagePath, StringComparison.OrdinalIgnoreCase))
        {
            _thumbnailPath = l.ImagePath;
            Thumbnail = l.ImagePath is null ? null : ImageLoader.LoadThumbnail(l.ImagePath, 480);
        }
        _loading = false;
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

    partial void OnImageWidthChanged(double value)
    {
        if (!_loading)
            _widget.SetWidth(value);
    }

    partial void OnImageOpacityChanged(double value)
    {
        if (!_loading)
            _widget.Update(l => l.Opacity = Math.Clamp(value, 0.1, 1));
    }

    partial void OnCornerRadiusChanged(double value)
    {
        if (!_loading)
            _widget.Update(l => l.CornerRadius = Math.Max(0, value));
    }

    [RelayCommand]
    private void ChooseImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a picture or GIF",
            Filter = "Pictures and GIFs|" + string.Join(";", ImageStore.SupportedExtensions.Select(e => "*" + e)) + "|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            _widget.SetImage(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = "Could not copy the image: " + ex.Message;
        }
    }

    [RelayCommand]
    private void RemoveImage() => _widget.RemoveImage();
}
