using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using Status = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace MoniMS.Core.Media;

/// <summary>
/// Windows 미디어 제어(GlobalSystemMediaTransportControls)로 재생 중인 앱을 지켜보고 조작한다.
/// 작업표시줄 음량 플라이아웃에 나오는 것과 같은 정보라서, Spotify·브라우저(YouTube, YouTube Music)·
/// YouTube Music 앱 등 그곳에 나오는 앱이면 모두 된다.
/// </summary>
/// <remarks>
/// 상태는 모두 <see cref="Acquire"/>를 처음 부른 스레드(UI)에서 바꾸고, <see cref="Changed"/>도 거기서 알린다.
/// Windows가 보내는 이벤트(스레드 풀)는 그 스레드로 넘긴다. 쓰는 곳이 없으면(위젯 숨김, 설정 창 닫힘) 구독을 끊는다.
/// </remarks>
public sealed class MediaSessions : IDisposable
{
    /// <summary>이보다 큰 앨범 아트는 받지 않는다.</summary>
    private const int MaxThumbnailBytes = 4 * 1024 * 1024;

    private readonly ILogger<MediaSessions> _logger;
    private readonly List<Entry> _entries = [];
    private SynchronizationContext? _context;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private int _users;
    private bool _starting;

    /// <summary>처음 세션 목록을 읽은 뒤부터 "방금 재생 시작"을 알아챈다 (시작할 때 이미 재생 중인 앱은 해당 없음).</summary>
    private bool _initialized;

    /// <summary>사용자가 위젯에서 직접 고른 세션. 다른 앱이 재생을 시작하면 풀린다.</summary>
    private string? _pinnedId;

    private int _updateQueued;

    public MediaSessions(ILogger<MediaSessions>? logger = null) =>
        _logger = logger ?? NullLogger<MediaSessions>.Instance;

    /// <summary>보여 줄 곡이나 세션 목록이 바뀌었을 때 (UI 스레드).</summary>
    public event EventHandler? Changed;

    /// <summary>위젯에 보여 줄 곡. 미디어 세션이 하나도 없으면 null.</summary>
    public NowPlaying? Current { get; private set; }

    /// <summary>지금 있는 미디어 세션 전부.</summary>
    public IReadOnlyList<MediaSessionInfo> Sessions { get; private set; } = [];

    /// <summary>Windows 미디어 제어를 쓸 수 없으면 false (예: 미디어 기능이 없는 Windows N 에디션).</summary>
    public bool IsAvailable { get; private set; } = true;

    /// <summary>쓰기 시작 (위젯이 보이거나 설정 창이 열림). 처음 부르면 세션을 읽기 시작한다.</summary>
    public void Acquire()
    {
        _context ??= SynchronizationContext.Current ?? new SynchronizationContext();
        if (++_users == 1)
            _ = StartAsync();
    }

    /// <summary>그만 쓴다. 쓰는 곳이 모두 그만 쓰면 Windows 이벤트 구독을 끊는다.</summary>
    public void Release()
    {
        if (_users > 0 && --_users == 0)
            Stop();
    }

    // ================= 조작 =================

    public async Task PlayPauseAsync()
    {
        if (Shown() is not { } e)
            return;
        await RunAsync(e, async s =>
        {
            var info = s.GetPlaybackInfo();
            var playing = info.PlaybackStatus == Status.Playing;
            var c = info.Controls;
            if (playing && c.IsPauseEnabled)
                await s.TryPauseAsync();
            else if (!playing && c.IsPlayEnabled)
                await s.TryPlayAsync();
            else
                await s.TryTogglePlayPauseAsync();
        });
    }

    public async Task NextAsync()
    {
        if (Shown() is { } e)
            await RunAsync(e, async s => await s.TrySkipNextAsync());
    }

    public async Task PreviousAsync()
    {
        if (Shown() is { } e)
            await RunAsync(e, async s => await s.TrySkipPreviousAsync());
    }

    /// <summary>곡의 원하는 위치로 옮긴다 (앱이 허락할 때만, <see cref="NowPlaying.CanSeek"/>).</summary>
    /// <param name="position">곡 처음부터의 위치.</param>
    public async Task SeekAsync(TimeSpan position)
    {
        if (Shown() is not { } e)
            return;
        await RunAsync(e, async s =>
        {
            var t = s.GetTimelineProperties();
            var duration = t.EndTime - t.StartTime;
            if (position < TimeSpan.Zero)
                position = TimeSpan.Zero;
            if (duration > TimeSpan.Zero && position > duration)
                position = duration;
            // 앱이 새 위치를 알려 주기 전에도 막대가 바로 그 자리에 있도록
            e.Sought = (position, DateTimeOffset.Now);
            Update();
            if (!await s.TryChangePlaybackPositionAsync((t.StartTime + position).Ticks))
                e.Sought = null; // 앱이 거절 → 원래 위치로
        });
    }

    /// <summary>다음 앱의 세션을 보여 준다 (여러 앱이 미디어 세션을 갖고 있을 때).</summary>
    public void ShowNextSession()
    {
        if (MediaSessionPicker.Next(Sessions, Current?.SessionId) is not { } id)
            return;
        _pinnedId = id;
        Update();
    }

    private async Task RunAsync(Entry entry, Func<Session, Task> action)
    {
        try
        {
            await action(entry.Session);
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Media command for {App} failed: {Message}", entry.AppId, ex.Message); // 그사이 앱이 꺼짐 등
        }
        QueueUpdate(); // 대개 이벤트가 오지만, 안 보내는 앱도 있다
    }

    // ================= 시작 / 끝 =================

    private async Task StartAsync()
    {
        if (_manager is not null || _starting)
            return;
        _starting = true;
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_users == 0 || _manager is not null)
                return; // 그사이 그만 씀
            _manager = manager;
            IsAvailable = true;
            manager.SessionsChanged += OnSessionsChanged;
            manager.CurrentSessionChanged += OnCurrentSessionChanged;
            RefreshSessions();
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            _logger.LogWarning(ex, "Windows media controls are not available");
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _starting = false;
        }
    }

    private void Stop()
    {
        if (_manager is { } manager)
        {
            manager.SessionsChanged -= OnSessionsChanged;
            manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }
        _manager = null;
        foreach (var e in _entries)
            e.Unsubscribe();
        _entries.Clear();
        _initialized = false;
        _pinnedId = null;
        Current = null;
        Sessions = [];
    }

    public void Dispose() => Stop();

    // ================= Windows 이벤트 (스레드 풀) → UI 스레드 =================

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        Post(RefreshSessions);

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        QueueUpdate();

    /// <summary>재생 상태·위치 이벤트는 몰려서 오기도 해서 한 번으로 모은다.</summary>
    private void QueueUpdate()
    {
        if (Interlocked.Exchange(ref _updateQueued, 1) == 0)
            Post(() =>
            {
                _updateQueued = 0;
                Update();
            });
    }

    private void Post(Action action) => _context?.Post(_ =>
    {
        if (_manager is not null)
            action();
    }, null);

    // ================= 세션 목록 =================

    private void RefreshSessions()
    {
        IReadOnlyList<Session> sessions;
        try
        {
            sessions = _manager!.GetSessions();
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Reading media sessions failed: {Message}", ex.Message);
            return;
        }

        var next = new List<Entry>();
        var load = new List<Entry>();
        foreach (var session in sessions)
        {
            if (_entries.FirstOrDefault(e => ReferenceEquals(e.Session, session)) is { } same)
            {
                next.Add(same);
                continue;
            }
            string appId;
            try
            {
                appId = session.SourceAppUserModelId ?? "";
            }
            catch (Exception)
            {
                continue; // 그사이 사라짐
            }
            // 같은 앱의 세션이 새 객체로 왔으면 상태(재생 중이었는지 등)는 이어 간다
            if (_entries.FirstOrDefault(e => e.AppId == appId && !next.Contains(e) && !sessions.Any(s => ReferenceEquals(s, e.Session))) is { } renewed)
            {
                renewed.Unsubscribe();
                renewed.Session = session;
                renewed.Subscribe(QueueUpdate, () => Post(() => _ = LoadPropertiesAsync(renewed)));
                next.Add(renewed);
                load.Add(renewed);
                continue;
            }
            // 같은 앱 ID가 둘이면 구별되게
            var id = appId;
            for (var n = 2; next.Any(e => e.Id == id) || _entries.Any(e => e.Id == id); n++)
                id = $"{appId}#{n}";
            var entry = new Entry(session, appId, id, MediaApps.Describe(appId));
            entry.Subscribe(QueueUpdate, () => Post(() => _ = LoadPropertiesAsync(entry)));
            next.Add(entry);
            load.Add(entry);
        }

        foreach (var gone in _entries.Except(next))
            gone.Unsubscribe();
        _entries.Clear();
        _entries.AddRange(next);
        Update();
        // 목록을 바꾼 뒤에 읽는다: Windows가 이미 갖고 있는 정보는 곧바로(동기로) 돌아오기도 한다
        foreach (var entry in load)
            _ = LoadPropertiesAsync(entry);
    }

    /// <summary>제목·아티스트·앨범 아트 정보 (곡이 바뀔 때마다).</summary>
    private async Task LoadPropertiesAsync(Entry entry)
    {
        var version = ++entry.PropertiesVersion;
        GlobalSystemMediaTransportControlsSessionMediaProperties? props;
        try
        {
            props = await entry.Session.TryGetMediaPropertiesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Reading media properties of {App} failed: {Message}", entry.AppId, ex.Message);
            return;
        }
        if (version != entry.PropertiesVersion || !_entries.Contains(entry))
            return; // 그사이 또 바뀜
        entry.Title = props?.Title?.Trim() ?? "";
        entry.Artist = props?.Artist?.Trim() is { Length: > 0 } artist ? artist : props?.AlbumArtist?.Trim() ?? "";
        entry.Album = props?.AlbumTitle?.Trim() ?? "";
        entry.ThumbnailSource = props?.Thumbnail;
        entry.Thumbnail = null;
        entry.ThumbnailLoaded = false;
        Update();
    }

    /// <summary>보이는 세션의 앨범 아트만 받는다.</summary>
    private async Task LoadThumbnailAsync(Entry entry)
    {
        entry.ThumbnailLoaded = true;
        var version = entry.PropertiesVersion;
        if (entry.ThumbnailSource is not { } source)
            return;
        byte[]? bytes = null;
        try
        {
            using var stream = await source.OpenReadAsync();
            if (stream.Size is > 0 and <= MaxThumbnailBytes)
            {
                using var reader = new DataReader(stream.GetInputStreamAt(0));
                var loaded = await reader.LoadAsync((uint)stream.Size);
                bytes = new byte[loaded];
                reader.ReadBytes(bytes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Reading album art of {App} failed: {Message}", entry.AppId, ex.Message);
        }
        if (version != entry.PropertiesVersion || !_entries.Contains(entry))
            return;
        entry.Thumbnail = bytes;
        Update();
    }

    // ================= 보여 줄 곡 =================

    private Entry? Shown() => _entries.FirstOrDefault(e => e.Id == Current?.SessionId);

    private void Update()
    {
        if (_manager is null)
            return;
        string? started = null;
        var infos = new List<MediaSessionInfo>(_entries.Count);
        foreach (var e in _entries)
        {
            Status status;
            try
            {
                status = e.Session.GetPlaybackInfo()?.PlaybackStatus ?? Status.Closed;
            }
            catch (Exception)
            {
                status = Status.Closed; // 앱이 꺼지는 중 (곧 SessionsChanged가 온다)
            }
            var playing = status == Status.Playing;
            if (playing && !e.WasPlaying && _initialized)
                started ??= e.Id;
            // 곡이 넘어가는 사이(Changing)는 계속 재생 중으로 본다 (곡마다 "재생 시작"으로 치지 않도록)
            e.WasPlaying = playing || (e.WasPlaying && status == Status.Changing);
            infos.Add(new MediaSessionInfo(e.Id, e.App.Name, playing, e.Title, e.Artist));
        }
        _initialized = true;

        if (started is not null || (_pinnedId is not null && infos.All(s => s.Id != _pinnedId)))
            _pinnedId = null; // 다른 앱이 재생을 시작했거나 고른 앱이 꺼짐
        string? systemCurrent = null;
        try
        {
            // GetCurrentSession은 GetSessions와 다른 래퍼 객체를 돌려주므로 앱 ID로 찾는다
            var current = _manager.GetCurrentSession();
            var currentAppId = current?.SourceAppUserModelId;
            systemCurrent = (_entries.FirstOrDefault(e => ReferenceEquals(e.Session, current))
                ?? _entries.FirstOrDefault(e => e.AppId == currentAppId))?.Id;
        }
        catch (Exception)
        {
        }

        var shownId = MediaSessionPicker.Pick(infos, systemCurrent, _pinnedId, Current?.SessionId, started);
        if (shownId != Current?.SessionId)
            _logger.LogInformation("Media widget shows {App} (was {Previous}, started {Started}, picked {Pinned}, Windows current {Current})",
                shownId ?? "nothing", Current?.SessionId ?? "nothing", started ?? "-", _pinnedId ?? "-", systemCurrent ?? "-");
        var shown = _entries.FirstOrDefault(e => e.Id == shownId);
        Sessions = infos;
        Current = shown is null ? null : Snapshot(shown);
        if (shown is { ThumbnailLoaded: false })
            _ = LoadThumbnailAsync(shown);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private NowPlaying Snapshot(Entry e)
    {
        bool playing = false, canPlayPause = false, canNext = false, canPrevious = false, canSeek = false;
        double rate = 1;
        TimeSpan position = TimeSpan.Zero, duration = TimeSpan.Zero;
        var updatedAt = DateTimeOffset.MinValue;
        try
        {
            var info = e.Session.GetPlaybackInfo();
            var c = info.Controls;
            playing = info.PlaybackStatus == Status.Playing;
            canPlayPause = c.IsPlayEnabled || c.IsPauseEnabled || c.IsPlayPauseToggleEnabled;
            canNext = c.IsNextEnabled;
            canPrevious = c.IsPreviousEnabled;
            canSeek = c.IsPlaybackPositionEnabled;
            rate = info.PlaybackRate ?? 1;

            var t = e.Session.GetTimelineProperties();
            duration = t.EndTime - t.StartTime;
            position = t.Position - t.StartTime;
            updatedAt = t.LastUpdatedTime;
            if (e.Sought is { } sought)
            {
                bool pending;
                (position, updatedAt, pending) = PlaybackTimeline.AfterSeek(position, updatedAt, sought.Position, sought.At, DateTimeOffset.Now);
                if (!pending)
                    e.Sought = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Reading playback state of {App} failed: {Message}", e.AppId, ex.Message);
        }
        return new NowPlaying(e.Id, e.App, e.Title, e.Artist, e.Album, playing, canPlayPause, canNext, canPrevious, canSeek,
            position, duration, updatedAt, rate, e.Thumbnail);
    }

    private sealed class Entry(Session session, string appId, string id, MediaAppInfo app)
    {
        private TypedEventHandler<Session, PlaybackInfoChangedEventArgs>? _onPlayback;
        private TypedEventHandler<Session, TimelinePropertiesChangedEventArgs>? _onTimeline;
        private TypedEventHandler<Session, MediaPropertiesChangedEventArgs>? _onProperties;

        public Session Session { get; set; } = session;
        public string AppId { get; } = appId;
        public string Id { get; } = id;
        public MediaAppInfo App { get; } = app;

        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public IRandomAccessStreamReference? ThumbnailSource { get; set; }
        public byte[]? Thumbnail { get; set; }
        public bool ThumbnailLoaded { get; set; }
        public bool WasPlaying { get; set; }
        public int PropertiesVersion { get; set; }

        /// <summary>위젯에서 옮긴 위치와 그 시각. 앱이 새 위치를 알려 주면 지운다.</summary>
        public (TimeSpan Position, DateTimeOffset At)? Sought { get; set; }

        public void Subscribe(Action stateChanged, Action propertiesChanged)
        {
            _onPlayback = (_, _) => stateChanged();
            _onTimeline = (_, _) => stateChanged();
            _onProperties = (_, _) => propertiesChanged();
            try
            {
                Session.PlaybackInfoChanged += _onPlayback;
                Session.TimelinePropertiesChanged += _onTimeline;
                Session.MediaPropertiesChanged += _onProperties;
            }
            catch (Exception)
            {
                // 그사이 사라진 세션
            }
        }

        public void Unsubscribe()
        {
            try
            {
                if (_onPlayback is not null)
                    Session.PlaybackInfoChanged -= _onPlayback;
                if (_onTimeline is not null)
                    Session.TimelinePropertiesChanged -= _onTimeline;
                if (_onProperties is not null)
                    Session.MediaPropertiesChanged -= _onProperties;
            }
            catch (Exception)
            {
                // 이미 사라진 세션
            }
            _onPlayback = null;
            _onTimeline = null;
            _onProperties = null;
        }
    }
}
