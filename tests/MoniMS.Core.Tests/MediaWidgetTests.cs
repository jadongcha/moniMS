using System.Text.Json;
using MoniMS.Core.Desktop;
using MoniMS.Core.Media;
using MoniMS.Core.Presets;
using MoniMS.Core.Settings;

namespace MoniMS.Core.Tests;

public sealed class MediaWidgetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "moniMS-media-" + Guid.NewGuid().ToString("N"));

    public MediaWidgetTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // ---------- 앱 알아보기 ----------

    [Theory]
    [InlineData("Spotify.exe", "Spotify", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify", "Spotify")] // 스토어판
    [InlineData("Chrome", "Chrome", "chrome")]
    [InlineData("MSEdge", "Edge", "msedge")]
    [InlineData("308046B0AF4A39CB", "Firefox", "firefox")]
    [InlineData("Chrome._crx_cinhimbnkkaeohfgghhklpknlkffjgod", "YouTube Music", "chrome")] // Chrome에 설치한 YouTube Music
    [InlineData("MSEdge._crx_cinhimbnkkaeohfgghhklpknlkffjgod", "YouTube Music", "msedge")]
    [InlineData("com.github.th-ch.youtube-music", "YouTube Music", "YouTube Music")] // YouTube Music 데스크톱 앱
    [InlineData(@"C:\Program Files\foobar2000\foobar2000.exe", "foobar2000", "foobar2000")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Media Player", "Microsoft.Media.Player")]
    [InlineData("Contoso.Player_abc123!Player", "Player", "Player")]
    public void Apps_are_recognized_from_their_app_id(string appId, string name, string process)
    {
        var app = MediaApps.Describe(appId);
        Assert.Equal(name, app.Name);
        Assert.Equal([process], app.ProcessNames);
    }

    [Fact]
    public void Store_app_named_App_uses_the_package_name_and_has_no_process_guess()
    {
        var app = MediaApps.Describe("Contoso.MusicPlayer_abc123!App");
        Assert.Equal("MusicPlayer", app.Name);
        Assert.Empty(app.ProcessNames); // 음량은 바꿀 수 없다고 안내
    }

    [Fact]
    public void Unknown_app_id_is_shown_as_is()
    {
        Assert.Equal("Unknown app", MediaApps.Describe("").Name);
        Assert.Equal("SomePlayer", MediaApps.Describe("SomePlayer").Name);
        Assert.Empty(MediaApps.Describe("SomePlayer").ProcessNames);
    }

    // ---------- 재생 위치 ----------

    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Position_moves_on_while_playing()
    {
        var p = PlaybackTimeline.Estimate(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(3), T0, playing: true, rate: 1, T0.AddSeconds(12));
        Assert.Equal(TimeSpan.FromSeconds(42), p);
    }

    [Fact]
    public void Position_stays_while_paused()
    {
        var p = PlaybackTimeline.Estimate(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(3), T0, playing: false, rate: 1, T0.AddSeconds(12));
        Assert.Equal(TimeSpan.FromSeconds(30), p);
    }

    [Fact]
    public void Position_follows_playback_speed_and_stops_at_the_end()
    {
        Assert.Equal(TimeSpan.FromSeconds(50),
            PlaybackTimeline.Estimate(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(3), T0, true, 2, T0.AddSeconds(10)));
        Assert.Equal(TimeSpan.FromMinutes(3),
            PlaybackTimeline.Estimate(TimeSpan.FromSeconds(170), TimeSpan.FromMinutes(3), T0, true, 1, T0.AddSeconds(30)));
    }

    [Fact]
    public void Position_is_not_extrapolated_without_an_update_time()
    {
        // 위치를 알려 준 시각이 없는 앱 (기본값) → 그대로
        var p = PlaybackTimeline.Estimate(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(3), DateTimeOffset.MinValue, true, 1, T0);
        Assert.Equal(TimeSpan.FromSeconds(30), p);
    }

    [Fact]
    public void Right_after_a_seek_the_new_position_is_used_until_the_app_reports_it()
    {
        var seekAt = T0.AddSeconds(5);
        // 앱이 아직 옮기기 전 위치(30초, T0에 알림)를 들고 있다 → 옮긴 위치(2분)
        var (pos, at, pending) = PlaybackTimeline.AfterSeek(TimeSpan.FromSeconds(30), T0, TimeSpan.FromMinutes(2), seekAt, seekAt.AddSeconds(1));
        Assert.Equal((TimeSpan.FromMinutes(2), seekAt, true), (pos, at, pending));

        // 앱이 옮긴 뒤의 위치를 알려 줌 → 앱의 값
        var reportedAt = seekAt.AddMilliseconds(300);
        (pos, at, pending) = PlaybackTimeline.AfterSeek(TimeSpan.FromSeconds(121), reportedAt, TimeSpan.FromMinutes(2), seekAt, seekAt.AddSeconds(1));
        Assert.Equal((TimeSpan.FromSeconds(121), reportedAt, false), (pos, at, pending));
    }

    [Fact]
    public void A_seek_the_app_never_confirms_is_dropped_after_a_while()
    {
        var seekAt = T0.AddSeconds(5);
        var (pos, _, pending) = PlaybackTimeline.AfterSeek(TimeSpan.FromSeconds(30), T0, TimeSpan.FromMinutes(2), seekAt, seekAt + PlaybackTimeline.SeekGrace + TimeSpan.FromSeconds(1));
        Assert.False(pending);
        Assert.Equal(TimeSpan.FromSeconds(30), pos);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(187, "3:07")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-5, "0:00")]
    public void Time_is_formatted_like_a_player(int seconds, string expected) =>
        Assert.Equal(expected, PlaybackTimeline.Format(TimeSpan.FromSeconds(seconds)));

    // ---------- 보여 줄 세션 고르기 ----------

    private static MediaSessionInfo S(string id, bool playing) => new(id, id, playing, "", "");

    [Fact]
    public void Nothing_to_show_without_sessions() =>
        Assert.Null(MediaSessionPicker.Pick([], null, null, null, null));

    [Fact]
    public void At_start_the_playing_app_is_shown()
    {
        Assert.Equal("chrome", MediaSessionPicker.Pick([S("spotify", false), S("chrome", true)], "spotify", null, null, null));
        Assert.Equal("spotify", MediaSessionPicker.Pick([S("spotify", false), S("chrome", false)], "spotify", null, null, null));
    }

    [Fact]
    public void An_app_that_starts_playing_is_shown_even_over_a_picked_one()
    {
        var sessions = new[] { S("spotify", true), S("chrome", true) };
        Assert.Equal("chrome", MediaSessionPicker.Pick(sessions, "spotify", pinnedId: "spotify", shownId: "spotify", startedId: "chrome"));
    }

    [Fact]
    public void A_picked_app_stays_while_nothing_new_starts()
    {
        var sessions = new[] { S("spotify", true), S("chrome", false) };
        Assert.Equal("chrome", MediaSessionPicker.Pick(sessions, "spotify", pinnedId: "chrome", shownId: "chrome", startedId: null));
    }

    [Fact]
    public void Shown_app_stays_while_both_play()
    {
        // 두 앱이 모두 재생 중이어도 오가지 않는다
        Assert.Equal("spotify", MediaSessionPicker.Pick([S("spotify", true), S("chrome", true)], "chrome", null, "spotify", null));
    }

    [Fact]
    public void Paused_app_stays_so_it_can_be_resumed_from_the_widget()
    {
        // 위젯에서 일시 정지했는데 뒤에서 다른 앱이 재생 중이라고 넘어가면 다시 재생할 수 없다
        Assert.Equal("spotify", MediaSessionPicker.Pick([S("spotify", false), S("chrome", true)], "chrome", null, "spotify", null));
        Assert.Equal("spotify", MediaSessionPicker.Pick([S("spotify", false), S("chrome", false)], "chrome", null, "spotify", null));
    }

    [Fact]
    public void Shown_app_that_closed_is_replaced()
    {
        Assert.Equal("chrome", MediaSessionPicker.Pick([S("chrome", false)], null, "spotify", "spotify", null));
    }

    [Fact]
    public void Next_cycles_through_the_sessions()
    {
        var sessions = new[] { S("a", false), S("b", true), S("c", false) };
        Assert.Equal("b", MediaSessionPicker.Next(sessions, "a"));
        Assert.Equal("a", MediaSessionPicker.Next(sessions, "c"));
        Assert.Equal("a", MediaSessionPicker.Next(sessions, "gone"));
        Assert.Null(MediaSessionPicker.Next([], null));
    }

    // ---------- 레이아웃 / 설정 ----------

    [Fact]
    public void Media_widget_starts_hidden_for_new_and_old_settings()
    {
        Assert.False(new AppSettings().MediaWidget.Visible);
        Assert.Null(new AppSettings().MediaWidget.AccentColor); // Windows 강조색
        var old = JsonSerializer.Deserialize<AppSettings>("""{ "version": 5 }""", JsonDefaults.Options)!;
        Assert.False(old.MediaWidget.Visible); // 업데이트하자마자 위젯이 갑자기 뜨지 않는다
    }

    [Fact]
    public void Settings_round_trip_keeps_the_media_widget()
    {
        var settings = new AppSettings
        {
            MediaWidget = { Visible = true, Left = 100, Top = 200, Style = WidgetStyle.Glass, VolumeTarget = MediaVolumeTarget.System, AccentColor = "#E85A2A" },
        };
        var json = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options)!.MediaWidget;

        Assert.True(loaded.Visible);
        Assert.Equal((100d, 200d), (loaded.Left, loaded.Top));
        Assert.Equal(WidgetStyle.Glass, loaded.Style);
        Assert.Equal(MediaVolumeTarget.System, loaded.VolumeTarget);
        Assert.Equal("#E85A2A", loaded.AccentColor);
        Assert.Contains("\"volumeTarget\": \"System\"", json, StringComparison.Ordinal);
    }

    // ---------- 프리셋 ----------

    private sealed class FakeMediaHost : IMediaWidgetHost
    {
        public MediaWidgetLayout Current { get; set; } = new() { Visible = true, Left = 10, Top = 20, Width = 400, Height = 170 };

        public List<MediaWidgetLayout> Applied { get; } = [];

        public MediaWidgetLayout GetCurrentLayout() => Current;

        public void ApplyLayout(MediaWidgetLayout layout) => Applied.Add(layout);
    }

    private sealed class NoWallpaper : IWallpaperService
    {
        public WallpaperSettings Capture(string dir) => new();
        public void Apply(WallpaperSettings s, string dir) { }
    }

    private sealed class NoTheme : IThemeService
    {
        public ThemeSettings Capture() => new();
        public void Apply(ThemeSettings s) { }
        public Rgb GetAccentColor() => Rgb.Parse("#123456");
    }

    private sealed class NoIcons : IDesktopIconService
    {
        public DesktopIconLayout Capture() => new();
        public IconApplyResult Apply(DesktopIconLayout l) => new(0, 0, false);
    }

    private sealed class NoWidget : IWidgetLayoutHost
    {
        public WidgetLayout GetCurrentLayout() => new();
        public void ApplyLayout(WidgetLayout layout) { }
    }

    private (PresetService Service, PresetStore Store, FakeMediaHost Host) Create()
    {
        var store = new PresetStore(Path.Combine(_root, "presets"));
        var host = new FakeMediaHost();
        return (new PresetService(store, new NoWallpaper(), new NoTheme(), new NoIcons(), new NoWidget(), mediaWidget: host), store, host);
    }

    [Fact]
    public void Preset_saves_and_restores_the_media_widget()
    {
        var (svc, store, host) = Create();
        var preset = svc.Capture("x");
        host.Current.Left = 999; // 저장한 뒤 바뀌어도 프리셋은 그대로

        var result = svc.Apply(store.Get(preset.Id)!);

        Assert.True(result.Applied.HasFlag(PresetParts.MediaWidget));
        var applied = Assert.Single(host.Applied);
        Assert.True(applied.Visible);
        Assert.Equal((10d, 20d, 400d, 170d), (applied.Left, applied.Top, applied.Width, applied.Height));
    }

    [Fact]
    public void Overwriting_a_preset_made_before_the_media_widget_adds_it()
    {
        var (svc, store, _) = Create();
        var old = new Preset { Name = "v3", SchemaVersion = 3, Theme = new ThemeSettings() };
        store.Save(old);

        svc.Overwrite(store.Get(old.Id)!);

        var saved = store.Get(old.Id)!;
        Assert.NotNull(saved.MediaWidget);
        Assert.Null(saved.FileWidget); // 파일 위젯은 스키마 3에서 이미 고를 수 있었다
        Assert.Equal(Preset.CurrentSchemaVersion, saved.SchemaVersion);
    }

    [Fact]
    public void Overwriting_a_new_preset_saved_without_media_widget_keeps_it_out()
    {
        var (svc, store, _) = Create();
        var preset = svc.Capture("theme only", PresetParts.Theme);

        svc.Overwrite(store.Get(preset.Id)!);

        Assert.Null(store.Get(preset.Id)!.MediaWidget);
    }
}
