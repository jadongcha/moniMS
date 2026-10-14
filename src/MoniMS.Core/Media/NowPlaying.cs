namespace MoniMS.Core.Media;

/// <summary>미디어 세션 하나 (Windows 미디어 제어에 나오는 앱 하나).</summary>
/// <param name="Id">앱 ID (AppUserModelID).</param>
public sealed record MediaSessionInfo(string Id, string AppName, bool IsPlaying, string Title, string Artist);

/// <summary>위젯에 보여 주는 곡.</summary>
/// <param name="Position">마지막으로 알려 준 재생 위치 (<paramref name="PositionUpdatedAt"/> 기준).</param>
/// <param name="Duration">곡 길이. 앱이 알려 주지 않으면 0.</param>
/// <param name="Thumbnail">앨범 아트 원본 바이트 (JPEG/PNG). 없으면 null.</param>
public sealed record NowPlaying(
    string SessionId,
    MediaAppInfo App,
    string Title,
    string Artist,
    string Album,
    bool IsPlaying,
    bool CanPlayPause,
    bool CanNext,
    bool CanPrevious,
    bool CanSeek,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset PositionUpdatedAt,
    double PlaybackRate,
    byte[]? Thumbnail)
{
    public bool HasTimeline => Duration > TimeSpan.Zero;

    /// <summary>지금 재생 위치 추정. 앱은 위치를 가끔(재생·멈춤·이동할 때)만 알려 주므로, 재생 중이면 그 뒤로 흐른 시간을 더한다.</summary>
    public TimeSpan EstimatePosition(DateTimeOffset now) =>
        PlaybackTimeline.Estimate(Position, Duration, PositionUpdatedAt, IsPlaying, PlaybackRate, now);
}

public static class PlaybackTimeline
{
    public static TimeSpan Estimate(TimeSpan position, TimeSpan duration, DateTimeOffset updatedAt, bool playing, double rate, DateTimeOffset now)
    {
        var pos = position;
        var elapsed = now - updatedAt;
        // 알려 준 시각이 없거나(0001년·1601년) 미래면 더하지 않는다
        if (playing && elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromDays(1))
            pos += elapsed * (rate > 0 ? rate : 1);
        if (pos < TimeSpan.Zero)
            pos = TimeSpan.Zero;
        return duration > TimeSpan.Zero && pos > duration ? duration : pos;
    }

    /// <summary>원하는 위치로 옮긴 뒤 앱이 새 위치를 알려 주기를 기다리는 최대 시간.</summary>
    public static readonly TimeSpan SeekGrace = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 위치를 옮긴 직후에는 옮긴 위치를 쓴다. 앱이 예전 위치를 아직 들고 있으면 막대가 되돌아갔다가 뛰기 때문.
    /// 앱이 옮긴 뒤의 위치를 알려 주었거나(<paramref name="reportedAt"/>가 옮긴 시각 이후) 한참 지났으면 앱의 값을 쓴다.
    /// </summary>
    /// <returns>쓸 위치와 그 시각, 그리고 아직 옮긴 위치를 쓰는 중인지.</returns>
    public static (TimeSpan Position, DateTimeOffset At, bool Pending) AfterSeek(
        TimeSpan reported, DateTimeOffset reportedAt, TimeSpan sought, DateTimeOffset soughtAt, DateTimeOffset now) =>
        reportedAt >= soughtAt || now - soughtAt > SeekGrace
            ? (reported, reportedAt, false)
            : (sought, soughtAt, true);

    /// <summary>"3:07", 한 시간이 넘으면 "1:02:03".</summary>
    public static string Format(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
            t = TimeSpan.Zero;
        var total = (long)t.TotalSeconds;
        var (h, m, s) = (total / 3600, total / 60 % 60, total % 60);
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }
}

/// <summary>여러 앱이 미디어 세션을 갖고 있을 때 위젯에 보여 줄 세션을 고른다.</summary>
public static class MediaSessionPicker
{
    /// <param name="sessions">지금 있는 세션 (Windows가 알려 준 순서).</param>
    /// <param name="systemCurrentId">Windows가 "현재"로 보는 세션 (가장 최근에 쓴 앱).</param>
    /// <param name="pinnedId">사용자가 위젯에서 직접 고른 세션.</param>
    /// <param name="shownId">지금 위젯에 보이는 세션.</param>
    /// <param name="startedId">방금 재생을 시작한 세션.</param>
    /// <returns>보여 줄 세션 ID. 세션이 없으면 null.</returns>
    public static string? Pick(IReadOnlyList<MediaSessionInfo> sessions, string? systemCurrentId, string? pinnedId,
        string? shownId, string? startedId)
    {
        if (sessions.Count == 0)
            return null;
        MediaSessionInfo? Find(string? id) => id is null ? null : sessions.FirstOrDefault(s => s.Id == id);

        // 방금 재생을 시작한 앱 → 지금 듣는 것이므로 그쪽으로 (직접 고른 것보다 우선)
        if (Find(startedId) is { IsPlaying: true } started)
            return started.Id;
        if (Find(pinnedId) is { } pinned)
            return pinned.Id;
        // 보이던 앱은 일시 정지해도 그대로 (위젯에서 멈췄다가 다시 재생할 수 있게, 두 앱 사이를 오가지 않게)
        if (Find(shownId) is { } shown)
            return shown.Id;
        if (Find(systemCurrentId) is { IsPlaying: true } current)
            return current.Id;
        if (sessions.FirstOrDefault(s => s.IsPlaying) is { } playing)
            return playing.Id;
        return (Find(systemCurrentId) ?? sessions[0]).Id;
    }

    /// <summary>위젯에서 앱 이름을 누르면 다음 세션으로.</summary>
    public static string? Next(IReadOnlyList<MediaSessionInfo> sessions, string? shownId)
    {
        if (sessions.Count == 0)
            return null;
        var i = sessions.ToList().FindIndex(s => s.Id == shownId);
        return sessions[(i + 1) % sessions.Count].Id;
    }
}
