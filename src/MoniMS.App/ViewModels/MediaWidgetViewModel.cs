using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoniMS.Core.Media;
using MoniMS.Core.Presets;

namespace MoniMS.App.ViewModels;

/// <summary>
/// 음악 위젯 내용: 지금 재생 중인 곡, 재생 조작, 음량.
/// 위젯이 보이는 동안만(<see cref="SetActive"/>) Windows 미디어 제어를 지켜본다.
/// </summary>
public sealed partial class MediaWidgetViewModel : ObservableObject, IDisposable
{
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";

    private readonly MediaSessions _media;
    private readonly AudioVolume _volume;

    /// <summary>재생 중일 때 1초마다 진행 막대를 움직인다 (앱은 위치를 가끔만 알려 준다).</summary>
    private readonly DispatcherTimer _clock;

    /// <summary>음량은 다른 곳(볼륨 믹서, 키보드)에서도 바뀌므로 가끔 다시 읽는다.</summary>
    private readonly DispatcherTimer _volumePoll;

    private bool _active;
    private bool _syncingVolume;
    private bool _syncingProgress;

    /// <summary>시간 막대의 동그라미를 끄는 중 (놓을 때 옮긴다). 그동안은 시계가 막대를 움직이지 않는다.</summary>
    private bool _seeking;
    private bool _seekMoved;
    private MediaVolumeTarget _volumeTarget;
    private string? _shownSessionId;
    private byte[]? _artBytes;
    private int _artVersion;

    public MediaWidgetViewModel(MediaSessions media, AudioVolume volume)
    {
        _media = media;
        _volume = volume;
        _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) =>
        {
            if (IsCovered?.Invoke() != true)
                UpdateClock();
        };
        _volumePoll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        _volumePoll.Tick += (_, _) =>
        {
            if (IsCovered?.Invoke() != true)
                RefreshVolume();
        };
        ShowEmpty();
    }

    [ObservableProperty] private bool _hasMedia;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _appName = "";
    [ObservableProperty] private bool _canSwitch;
    [ObservableProperty] private string? _switchToolTip;
    [ObservableProperty] private ImageSource? _art;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseGlyph), nameof(PlayPauseToolTip))]
    private bool _isPlaying;

    [ObservableProperty] private bool _canPlayPause;
    [ObservableProperty] private bool _canNext;
    [ObservableProperty] private bool _canPrevious;

    [ObservableProperty] private bool _hasTimeline;
    [ObservableProperty] private string _positionText = "";
    [ObservableProperty] private string _durationText = "";

    /// <summary>시간 막대 (0 ~ 1). 사용자가 바꾸면 그 위치로 옮긴다.</summary>
    [ObservableProperty] private double _progress;

    /// <summary>시간 막대로 위치를 옮길 수 있는지 (앱이 허락할 때만).</summary>
    [ObservableProperty] private bool _canSeek;

    [ObservableProperty] private string _seekToolTip = "";

    /// <summary>음량 (0 ~ 100).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeGlyph))]
    private double _volumeLevel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeGlyph))]
    private bool _isMuted;

    [ObservableProperty] private bool _volumeAvailable;
    [ObservableProperty] private string _volumeToolTip = "";

    /// <summary>
    /// 위젯 창이 다른 창에 완전히 가려졌는지. 가려진 동안은 진행 막대와 음량을 다시 그리지 않는다
    /// (바탕화면 위젯이라 대개 가려져 있고, 투명 창은 다시 그릴 때마다 CPU가 든다).
    /// </summary>
    public Func<bool>? IsCovered { get; set; }

    public string PlayPauseGlyph => IsPlaying ? PauseGlyph : PlayGlyph;

    public string PlayPauseToolTip => IsPlaying ? "Pause" : "Play";

    /// <summary>스피커 아이콘: 음소거 / 음량에 따라 물결 0 ~ 3개.</summary>
    public string VolumeGlyph => IsMuted ? ""
        : VolumeLevel <= 0 ? ""
        : VolumeLevel < 34 ? ""
        : VolumeLevel < 67 ? ""
        : "";

    /// <summary>위젯이 보이는 동안만 미디어 세션을 지켜보고 시계를 돌린다.</summary>
    public void SetActive(bool active)
    {
        if (active == _active)
            return;
        _active = active;
        if (active)
        {
            _media.Changed += OnMediaChanged;
            _media.Acquire();
            Refresh();
            _volumePoll.Start();
        }
        else
        {
            _media.Changed -= OnMediaChanged;
            _media.Release();
            _clock.Stop();
            _volumePoll.Stop();
        }
    }

    public void SetVolumeTarget(MediaVolumeTarget target)
    {
        if (target == _volumeTarget)
            return;
        _volumeTarget = target;
        RefreshVolume();
    }

    private void OnMediaChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var now = _media.Current;
        if (now is null)
        {
            ShowEmpty();
        }
        else
        {
            HasMedia = true;
            Title = now.Title.Length > 0 ? now.Title : now.App.Name;
            Subtitle = string.Join(" · ", new[] { now.Artist, now.Album }.Where(s => s.Length > 0).Distinct());
            AppName = now.App.Name;
            IsPlaying = now.IsPlaying;
            CanPlayPause = now.CanPlayPause;
            CanNext = now.CanNext;
            CanPrevious = now.CanPrevious;
            CanSeek = now.CanSeek && now.HasTimeline;
            SeekToolTip = CanSeek ? "Click or drag to jump to a point" : $"{now.App.Name} doesn't let other apps change the position";
            SetArt(now.Thumbnail);
        }

        var sessions = _media.Sessions;
        CanSwitch = sessions.Count > 1;
        SwitchToolTip = CanSwitch && MediaSessionPicker.Next(sessions, now?.SessionId) is { } next
            ? $"Switch to {sessions.First(s => s.Id == next).AppName}"
            : null;

        UpdateClock();
        if (IsPlaying && HasTimeline && _active)
            _clock.Start();
        else
            _clock.Stop();

        if (now?.SessionId != _shownSessionId)
        {
            _shownSessionId = now?.SessionId;
            RefreshVolume(); // 다른 앱 → 그 앱의 음량
        }
    }

    private void ShowEmpty()
    {
        HasMedia = false;
        Title = "Nothing playing";
        Subtitle = _media.IsAvailable
            ? "Play music in Spotify, YouTube or YouTube Music"
            : "Windows media controls aren't available on this PC";
        AppName = "";
        IsPlaying = false;
        CanPlayPause = CanNext = CanPrevious = CanSeek = false;
        SetArt(null);
    }

    private void UpdateClock()
    {
        if (_media.Current is not { HasTimeline: true } now)
        {
            HasTimeline = false;
            return;
        }
        HasTimeline = true;
        DurationText = PlaybackTimeline.Format(now.Duration);
        if (_seeking)
            return; // 끄는 동안은 손가락(마우스)이 가리키는 곳을 보여 준다
        var position = now.EstimatePosition(DateTimeOffset.Now);
        PositionText = PlaybackTimeline.Format(position);
        _syncingProgress = true;
        Progress = Math.Clamp(position / now.Duration, 0, 1);
        _syncingProgress = false;
    }

    // ================= 시간 막대로 옮기기 =================

    partial void OnProgressChanged(double value)
    {
        if (_syncingProgress || _media.Current is not { HasTimeline: true } now)
            return;
        var target = now.Duration * Math.Clamp(value, 0, 1);
        PositionText = PlaybackTimeline.Format(target);
        if (_seeking)
            _seekMoved = true; // 놓을 때 옮긴다
        else
            _ = _media.SeekAsync(target); // 막대를 눌렀다
    }

    /// <summary>시간 막대의 동그라미를 잡았다.</summary>
    public void BeginSeek()
    {
        _seeking = true;
        _seekMoved = false;
    }

    /// <summary>동그라미를 놓았다: 움직였으면 그 위치로 옮긴다.</summary>
    public void EndSeek()
    {
        if (!_seeking)
            return;
        _seeking = false;
        if (_seekMoved && _media.Current is { HasTimeline: true } now)
            _ = _media.SeekAsync(now.Duration * Math.Clamp(Progress, 0, 1));
        else
            UpdateClock();
    }

    // ================= 앨범 아트 =================

    private void SetArt(byte[]? bytes)
    {
        if (ReferenceEquals(bytes, _artBytes))
            return;
        _artBytes = bytes;
        var version = ++_artVersion;
        if (bytes is null)
        {
            Art = null;
            return;
        }
        _ = DecodeArtAsync(bytes, version);
    }

    private async Task DecodeArtAsync(byte[] bytes, int version)
    {
        var image = await Task.Run(() => Decode(bytes));
        if (version == _artVersion)
            Art = image;
    }

    /// <summary>위젯 크기에 맞게 줄여서 디코딩 (원본은 수백~천 px).</summary>
    private static BitmapImage? Decode(byte[] bytes)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 256;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null; // 알 수 없는 형식
        }
    }

    // ================= 조작 =================

    [RelayCommand]
    private Task PlayPause() => _media.PlayPauseAsync();

    [RelayCommand]
    private Task Next() => _media.NextAsync();

    [RelayCommand]
    private Task Previous() => _media.PreviousAsync();

    [RelayCommand]
    private void SwitchSession() => _media.ShowNextSession();

    // ================= 음량 =================

    private IReadOnlyList<string> AppProcesses => _media.Current?.App.ProcessNames ?? [];

    /// <summary>지금 음량을 다시 읽는다 (앱이 바뀌었을 때, 마우스를 올렸을 때, 가끔).</summary>
    public void RefreshVolume()
    {
        VolumeState? state;
        if (_volumeTarget == MediaVolumeTarget.System)
        {
            state = AudioVolume.GetSystem();
            VolumeToolTip = "Windows volume · click the speaker to mute";
        }
        else if (_media.Current is { } now)
        {
            state = now.App.ProcessNames.Count > 0 ? _volume.GetApp(now.App.ProcessNames) : null;
            VolumeToolTip = state is not null ? $"{now.App.Name} volume · click the speaker to mute"
                : now.App.ProcessNames.Count == 0 ? $"MoniMS can't change the volume of {now.App.Name}. Choose Windows volume in Settings → Media widget."
                : $"{now.App.Name} hasn't played any sound yet";
        }
        else
        {
            state = null;
            VolumeToolTip = "Nothing playing";
        }

        _syncingVolume = true;
        VolumeAvailable = state is not null;
        if (state is { } s)
        {
            VolumeLevel = Math.Round(s.Level * 100);
            IsMuted = s.Muted;
        }
        _syncingVolume = false;
    }

    partial void OnVolumeLevelChanged(double value)
    {
        if (_syncingVolume)
            return;
        var level = (float)(Math.Clamp(value, 0, 100) / 100);
        var ok = _volumeTarget == MediaVolumeTarget.System ? AudioVolume.SetSystem(level) : _volume.SetApp(AppProcesses, level);
        if (ok && level > 0 && IsMuted)
            SetMutedSilently(false); // 막대를 올리면 음소거도 풀린다
    }

    [RelayCommand]
    private void ToggleMute()
    {
        var mute = !IsMuted;
        var ok = _volumeTarget == MediaVolumeTarget.System ? AudioVolume.SetSystemMute(mute) : _volume.SetAppMute(AppProcesses, mute);
        if (ok)
            SetMutedSilently(mute);
    }

    /// <summary>마우스 휠: 한 칸에 2씩 (Windows 음량 플라이아웃과 같게).</summary>
    public void NudgeVolume(int notches)
    {
        if (VolumeAvailable)
            VolumeLevel = Math.Clamp(VolumeLevel + notches * 2, 0, 100);
    }

    private void SetMutedSilently(bool muted)
    {
        _syncingVolume = true;
        IsMuted = muted;
        _syncingVolume = false;
    }

    public void Dispose()
    {
        SetActive(false);
        _clock.Stop();
        _volumePoll.Stop();
    }
}
