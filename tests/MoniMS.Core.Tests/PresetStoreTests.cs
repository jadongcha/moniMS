using MoniMS.Core.Presets;

namespace MoniMS.Core.Tests;

public sealed class PresetStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "moniMS-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Save_and_load_roundtrip_keeps_all_parts()
    {
        var store = new PresetStore(_root);
        var preset = new Preset
        {
            Name = "게임 모드",
            Wallpaper = new WallpaperSettings
            {
                Position = WallpaperPosition.Span,
                Monitors = [new MonitorWallpaper { MonitorId = "mon1", Index = 0, ImageFile = "a.jpg", Width = 2560 }],
            },
            Theme = new ThemeSettings { AccentColor = "#E81123", AccentOnStartAndTaskbar = true },
            Icons = new DesktopIconLayout { ScreenWidth = 1920, Icons = [new IconPosition { Name = "휴지통", X = 10, Y = 20 }] },
            Widget = new WidgetLayout { Style = WidgetStyle.Glass, Sections = [WidgetSection.Cpu, WidgetSection.Gpu] },
        };
        store.Save(preset);

        var loaded = store.Get(preset.Id)!;
        Assert.Equal("게임 모드", loaded.Name);
        Assert.Equal(WallpaperPosition.Span, loaded.Wallpaper!.Position);
        Assert.Equal("a.jpg", loaded.Wallpaper.Monitors[0].ImageFile);
        Assert.Equal("#E81123", loaded.Theme!.AccentColor);
        Assert.Equal("휴지통", loaded.Icons!.Icons[0].Name);
        Assert.Equal([WidgetSection.Cpu, WidgetSection.Gpu], loaded.Widget!.Sections);
        Assert.Equal(PresetParts.Wallpaper | PresetParts.Theme | PresetParts.Icons | PresetParts.Widget, loaded.AvailableParts);
    }

    [Fact]
    public void Json_is_human_readable()
    {
        var store = new PresetStore(_root);
        var preset = new Preset { Name = "한글 이름", Widget = new WidgetLayout() };
        store.Save(preset);
        var json = File.ReadAllText(Path.Combine(_root, preset.Id, "preset.json"));
        Assert.Contains("한글 이름", json);
        Assert.Contains("\"style\": \"Dark\"", json);
        Assert.Contains("NaN", json); // 위치 미지정
    }

    [Fact]
    public void GetAll_skips_corrupted_files_and_Delete_removes_folder()
    {
        var store = new PresetStore(_root);
        var ok = new Preset { Name = "ok" };
        store.Save(ok);
        var broken = Path.Combine(_root, "broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "preset.json"), "{ not json");

        Assert.Single(store.GetAll());
        store.Delete(ok.Id);
        Assert.Empty(store.GetAll());
        Assert.False(Directory.Exists(Path.Combine(_root, ok.Id)));
    }

    [Theory]
    [InlineData("../evil", "evil")]
    [InlineData(@"..\..\x", "x")]
    [InlineData("abc-123_X", "abc-123_X")]
    public void Sanitize_blocks_path_traversal(string input, string expected) =>
        Assert.Equal(expected, PresetStore.Sanitize(input));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
