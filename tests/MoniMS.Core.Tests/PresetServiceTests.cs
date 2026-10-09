using MoniMS.Core.Desktop;
using MoniMS.Core.Presets;

namespace MoniMS.Core.Tests;

public sealed class PresetServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "moniMS-svc-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _calls = [];

    private sealed class FakeWallpaper(List<string> calls) : IWallpaperService
    {
        public WallpaperSettings Capture(string dir)
        {
            File.WriteAllText(Path.Combine(dir, "wallpaper_0_new.jpg"), "img");
            return new WallpaperSettings { Monitors = [new MonitorWallpaper { ImageFile = "wallpaper_0_new.jpg" }] };
        }

        public void Apply(WallpaperSettings s, string dir) => calls.Add("wallpaper");

        public void SetForAllMonitors(string imagePath, WallpaperPosition position) => calls.Add("wallpaper-all");
    }

    private sealed class FakeTheme(List<string> calls) : IThemeService
    {
        public ThemeSettings Capture() => new() { AccentColor = "#123456" };
        public void Apply(ThemeSettings s) => calls.Add("theme");
        public Rgb GetAccentColor() => Rgb.Parse("#123456");
    }

    private sealed class FakeIcons(List<string> calls, bool fail) : IDesktopIconService
    {
        public DesktopIconLayout Capture() => fail ? throw new InvalidOperationException("no desktop") : new();

        public IconApplyResult Apply(DesktopIconLayout l)
        {
            calls.Add("icons");
            return new IconApplyResult(3, 1, AutoArrangeEnabled: true);
        }
    }

    private sealed class FakeWidget(List<string> calls) : IWidgetLayoutHost
    {
        public WidgetLayout Current { get; set; } = new() { Width = 400 };
        public WidgetLayout GetCurrentLayout() => Current;

        public void ApplyLayout(WidgetLayout layout)
        {
            calls.Add("widget");
            Current = layout;
        }
    }

    private PresetService Create(out IPresetStore store, bool iconsFail = false)
    {
        store = new PresetStore(_root);
        return new PresetService(store, new FakeWallpaper(_calls), new FakeTheme(_calls),
            new FakeIcons(_calls, iconsFail), new FakeWidget(_calls));
    }

    [Fact]
    public void Capture_only_includes_requested_parts()
    {
        var svc = Create(out _);
        var p = svc.Capture("x", PresetParts.Theme | PresetParts.Widget);
        Assert.Null(p.Wallpaper);
        Assert.Null(p.Icons);
        Assert.Equal("#123456", p.Theme!.AccentColor);
        Assert.Equal(400, p.Widget!.Width);
    }

    [Fact]
    public void Capture_failure_of_one_part_does_not_break_others()
    {
        var svc = Create(out _, iconsFail: true);
        var p = svc.Capture("x");
        Assert.Null(p.Icons);
        Assert.NotNull(p.Theme);
        Assert.NotNull(p.Wallpaper);
    }

    [Fact]
    public void Apply_runs_theme_before_wallpaper_and_reports_icon_warnings()
    {
        var svc = Create(out _);
        var p = svc.Capture("x");
        _calls.Clear();

        var result = svc.Apply(p);

        Assert.Equal(["theme", "wallpaper", "icons", "widget"], _calls);
        Assert.Equal(PresetParts.Wallpaper | PresetParts.Theme | PresetParts.Icons | PresetParts.Widget, result.Applied); // 사진 위젯 없음
        Assert.Equal(2, result.Warnings.Count); // 자동 정렬 + 누락 아이콘
    }

    private sealed class ThreadRecordingTheme(List<string> calls) : IThemeService
    {
        public ThemeSettings Capture() => new() { AccentColor = "#123456" };
        public void Apply(ThemeSettings s) => calls.Add($"theme:{Thread.CurrentThread.GetApartmentState()}:{Environment.CurrentManagedThreadId}");
        public Rgb GetAccentColor() => Rgb.Parse("#123456");
    }

    [Fact]
    public async Task ApplyAsync_runs_desktop_parts_on_a_separate_sta_thread_in_the_same_order()
    {
        var svc = new PresetService(new PresetStore(_root), new FakeWallpaper(_calls), new ThreadRecordingTheme(_calls),
            new FakeIcons(_calls, false), new FakeWidget(_calls));
        var p = svc.Capture("x");
        _calls.Clear();
        var caller = Environment.CurrentManagedThreadId;

        var result = await svc.ApplyAsync(p);

        Assert.Equal(PresetParts.Wallpaper | PresetParts.Theme | PresetParts.Icons | PresetParts.Widget, result.Applied); // 사진 위젯 없음
        Assert.StartsWith("theme:STA:", _calls[0], StringComparison.Ordinal);
        Assert.NotEqual($"theme:STA:{caller}", _calls[0]);
        Assert.Equal(["wallpaper", "icons", "widget"], _calls[1..]);
        Assert.Equal(2, result.Warnings.Count);
    }

    [Fact]
    public void Theme_apply_is_skipped_only_when_nothing_would_change()
    {
        var current = new ThemeSettings { AppsUseLightTheme = true, SystemUsesLightTheme = true, AccentColor = "#0078D4" };
        Assert.True(ThemeService.IsCurrent(current, new ThemeSettings { AppsUseLightTheme = true, SystemUsesLightTheme = true, AccentColor = "#0078d4" }));
        // 라이트 모드에선 작업표시줄 강조색이 꺼진 채로 저장되므로 같은 상태
        Assert.True(ThemeService.IsCurrent(current, new ThemeSettings
        {
            AppsUseLightTheme = true, SystemUsesLightTheme = true, AccentOnStartAndTaskbar = true, AccentColor = "#0078D4",
        }));
        Assert.False(ThemeService.IsCurrent(current, new ThemeSettings { AppsUseLightTheme = false, SystemUsesLightTheme = true, AccentColor = "#0078D4" }));
        Assert.False(ThemeService.IsCurrent(current, new ThemeSettings { AppsUseLightTheme = true, SystemUsesLightTheme = true, AccentColor = "#FF0000" }));
    }

    [Fact]
    public void Overwrite_keeps_id_and_removes_old_images()
    {
        var svc = Create(out var store);
        var p = svc.Capture("x");
        var dir = store.GetPresetDirectory(p.Id);
        File.WriteAllText(Path.Combine(dir, "old.jpg"), "old");

        svc.Overwrite(p);

        Assert.Equal(p.Id, store.Get(p.Id)!.Id);
        Assert.False(File.Exists(Path.Combine(dir, "old.jpg")));
        Assert.True(File.Exists(Path.Combine(dir, "wallpaper_0_new.jpg")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
