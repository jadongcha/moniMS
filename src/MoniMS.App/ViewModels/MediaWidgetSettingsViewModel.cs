using CommunityToolkit.Mvvm.ComponentModel;
using MoniMS.App.Services;
using MoniMS.Core.Desktop;
using MoniMS.Core.Media;
using MoniMS.Core.Presets;

namespace MoniMS.App.ViewModels;

/// <summary>설정 창 "Media widget" 탭: 표시, 이동·크기 조절, 음량 대상, 스타일·불투명도, 찾은 플레이어.</summary>
public sealed partial class MediaWidgetSettingsViewModel : ObservableObject
{
    private readonly MediaWidgetController _widget;
    private readonly MediaSessions _media;
    private bool _loading;

    public MediaWidgetSettingsViewModel(MediaWidgetController widget, MediaSessions media, IThemeService theme)
    {
        _widget = widget;
        _media = media;
        ButtonColor = new ColorPickerViewModel("button color", theme.GetAccentColor, hex => _widget.Update(l => l.AccentColor = hex));
        Load();
        _widget.LayoutChanged += OnLayoutChanged;
        // 설정 창이 열려 있는 동안 지금 재생 중인 앱을 보여 준다 (위젯을 켜기 전에 잘 잡히는지 확인용)
        _media.Changed += OnMediaChanged;
        _media.Acquire();
        UpdatePlayers();
    }

    /// <summary>창이 닫힐 때 호출 (이벤트 구독 해제).</summary>
    public void Detach()
    {
        _widget.LayoutChanged -= OnLayoutChanged;
        _media.Changed -= OnMediaChanged;
        _media.Release();
    }

    [ObservableProperty] private bool _isShown;
    [ObservableProperty] private bool _editMode;
    [ObservableProperty] private WidgetStyle _style;
    [ObservableProperty] private double _widgetOpacity;

    /// <summary>재생 버튼 · 진행 막대 · 음량 막대 색.</summary>
    public ColorPickerViewModel ButtonColor { get; }

    /// <summary>음량 막대가 조절하는 것: 0 = 재생 중인 앱, 1 = Windows 전체 (<see cref="VolumeTargets"/> 순서).</summary>
    [ObservableProperty] private int _volumeTargetIndex;

    public IReadOnlyList<string> VolumeTargets { get; } = ["The app that's playing", "Windows volume"];

    /// <summary>지금 Windows 미디어 제어에 나오는 앱들 (한 줄에 하나).</summary>
    [ObservableProperty] private string _playersText = "";

    public IReadOnlyList<WidgetStyle> Styles { get; } = Enum.GetValues<WidgetStyle>();

    private void OnLayoutChanged(object? sender, EventArgs e) => Load();

    private void OnMediaChanged(object? sender, EventArgs e) => UpdatePlayers();

    private void Load()
    {
        _loading = true;
        var l = _widget.Layout;
        IsShown = l.Visible;
        EditMode = _widget.IsEditMode;
        Style = l.Style;
        WidgetOpacity = l.Opacity;
        ButtonColor.Load(l.AccentColor);
        VolumeTargetIndex = l.VolumeTarget == MediaVolumeTarget.App ? 0 : 1;
        _loading = false;
    }

    private void UpdatePlayers()
    {
        if (!_media.IsAvailable)
        {
            PlayersText = "Windows media controls aren't available on this PC.";
            return;
        }
        var sessions = _media.Sessions;
        PlayersText = sessions.Count == 0
            ? "None right now. Start playing something in Spotify, YouTube or YouTube Music."
            : string.Join("\n", sessions.Select(s =>
            {
                var song = string.Join(" – ", new[] { s.Title, s.Artist }.Where(t => t.Length > 0));
                return $"• {s.AppName}: {(s.IsPlaying ? "playing" : "paused")}" + (song.Length > 0 ? $" · {song}" : "");
            }));
    }

    partial void OnIsShownChanged(bool value)
    {
        if (!_loading)
            _widget.Update(l => l.Visible = value);
    }

    partial void OnEditModeChanged(bool value)
    {
        if (_loading)
            return;
        if (value && !_widget.Layout.Visible)
            _widget.Update(l => l.Visible = true); // 숨긴 채로는 옮길 수 없다
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

    partial void OnVolumeTargetIndexChanged(int value)
    {
        if (!_loading && value >= 0)
            _widget.Update(l => l.VolumeTarget = value == 0 ? MediaVolumeTarget.App : MediaVolumeTarget.System);
    }
}
