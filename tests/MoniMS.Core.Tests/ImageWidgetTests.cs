using System.Text.Json;
using MoniMS.Core.Desktop;
using MoniMS.Core.Presets;
using MoniMS.Core.Settings;

namespace MoniMS.Core.Tests;

public sealed class ImageWidgetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "moniMS-image-" + Guid.NewGuid().ToString("N"));

    public ImageWidgetTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private string MakeFile(string name, string content)
    {
        var path = Path.Combine(_root, "source", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    // ---------- 이미지 보관함 ----------

    [Fact]
    public void Store_copies_once_per_content_and_keeps_extension()
    {
        var store = new ImageStore(Path.Combine(_root, "images"));
        var a = store.Import(MakeFile("cat.GIF", "same bytes"));
        var b = store.Import(MakeFile("copy of cat.gif", "same bytes"));
        var c = store.Import(MakeFile("dog.png", "other bytes"));

        Assert.Equal(a, b); // 같은 내용은 한 번만
        Assert.NotEqual(a, c);
        Assert.EndsWith(".gif", a, StringComparison.Ordinal);
        Assert.True(store.Contains(a));
        Assert.Equal(a, store.Import(a)); // 이미 보관함 안이면 그대로
        Assert.Equal(2, Directory.GetFiles(store.RootDirectory).Length);
    }

    [Fact]
    public void Store_cleanup_keeps_only_the_current_image()
    {
        var store = new ImageStore(Path.Combine(_root, "images"));
        var keep = store.Import(MakeFile("a.png", "a"));
        store.Import(MakeFile("b.png", "b"));

        store.RemoveUnused(keep);

        Assert.Equal([keep], Directory.GetFiles(store.RootDirectory));
        store.RemoveUnused(null);
        Assert.Empty(Directory.GetFiles(store.RootDirectory));
    }

    [Theory]
    [InlineData("photo.JPG", true)]
    [InlineData("anim.gif", true)]
    [InlineData("pic.webp", true)]
    [InlineData("movie.mp4", false)]
    [InlineData("notes.txt", false)]
    public void Recognizes_supported_image_types(string file, bool expected) =>
        Assert.Equal(expected, ImageStore.IsSupported(file));

    [Fact]
    public void Settings_file_does_not_store_computed_values()
    {
        var json = JsonSerializer.Serialize(new AppSettings { ImageWidget = { ImagePath = @"C:\x.png" } }, JsonDefaults.Options);
        Assert.Contains("imagePath", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hasImage", json, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- 프리셋 ----------

    private sealed class FakeImageHost(string imagePath) : IImageWidgetHost
    {
        public ImageWidgetLayout Current { get; set; } = new()
        {
            ImagePath = imagePath, ImageName = "cat.gif", Left = 100, Top = 50, Width = 320, Height = 180, CornerRadius = 4,
        };

        public List<ImageWidgetLayout> Applied { get; } = [];

        public ImageWidgetLayout GetCurrentLayout() => Current;

        public void ApplyLayout(ImageWidgetLayout layout) => Applied.Add(layout);
    }

    private sealed class NoWallpaper : IWallpaperService
    {
        public WallpaperSettings Capture(string dir)
        {
            File.WriteAllText(Path.Combine(dir, "wallpaper_0_x.jpg"), "wall");
            return new WallpaperSettings { Monitors = [new MonitorWallpaper { ImageFile = "wallpaper_0_x.jpg" }] };
        }

        public void Apply(WallpaperSettings s, string dir) { }
        public void SetForAllMonitors(string imagePath, WallpaperPosition position) { }
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

    private (PresetService Service, PresetStore Store, FakeImageHost Host) Create()
    {
        var store = new PresetStore(Path.Combine(_root, "presets"));
        var host = new FakeImageHost(MakeFile("cat.gif", "GIF89a frames"));
        return (new PresetService(store, new NoWallpaper(), new NoTheme(), new NoIcons(), new NoWidget(), imageWidget: host), store, host);
    }

    [Fact]
    public void Capture_copies_the_image_into_the_preset_and_apply_hands_back_a_full_path()
    {
        var (svc, store, host) = Create();
        var preset = svc.Capture("x");

        var saved = preset.ImageWidget!;
        Assert.True(preset.AvailableParts.HasFlag(PresetParts.ImageWidget));
        Assert.StartsWith("image_", saved.ImagePath, StringComparison.Ordinal);
        Assert.EndsWith(".gif", saved.ImagePath, StringComparison.Ordinal);
        Assert.Equal((100d, 50d, 320d, 180d, 4d), (saved.Left, saved.Top, saved.Width, saved.Height, saved.CornerRadius));
        var copy = Path.Combine(store.GetPresetDirectory(preset.Id), saved.ImagePath!);
        Assert.Equal("GIF89a frames", File.ReadAllText(copy));

        // 원본을 지워도 프리셋은 자기 복사본으로 적용된다
        File.Delete(host.Current.ImagePath!);
        var result = svc.Apply(store.Get(preset.Id)!);

        Assert.True(result.Applied.HasFlag(PresetParts.ImageWidget));
        Assert.Empty(result.Warnings);
        Assert.Equal(copy, Assert.Single(host.Applied).ImagePath);
    }

    [Fact]
    public void Overwrite_keeps_wallpaper_and_image_copies_and_removes_stale_ones()
    {
        var (svc, store, _) = Create();
        var preset = svc.Capture("x");
        var dir = store.GetPresetDirectory(preset.Id);
        File.WriteAllText(Path.Combine(dir, "image_old.gif"), "old");
        Thread.Sleep(2);

        svc.Overwrite(preset);

        var files = Directory.GetFiles(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        string[] expected = ["preset.json", preset.ImageWidget!.ImagePath!, "wallpaper_0_x.jpg"];
        Assert.Equal(expected.Order(StringComparer.Ordinal), files);
    }

    [Fact]
    public void Widget_without_image_is_saved_and_applied_as_empty()
    {
        var (svc, store, host) = Create();
        host.Current = new ImageWidgetLayout { ImagePath = null, Visible = true };

        var preset = svc.Capture("x", PresetParts.ImageWidget);
        svc.Apply(store.Get(preset.Id)!);

        Assert.Null(preset.ImageWidget!.ImagePath);
        Assert.Null(Assert.Single(host.Applied).ImagePath);
    }

    [Fact]
    public void Missing_image_in_preset_hides_the_widget_with_a_warning()
    {
        var (svc, store, host) = Create();
        var preset = svc.Capture("x");
        File.Delete(Path.Combine(store.GetPresetDirectory(preset.Id), preset.ImageWidget!.ImagePath!));

        var result = svc.Apply(store.Get(preset.Id)!);

        Assert.Null(Assert.Single(host.Applied).ImagePath);
        Assert.Contains(result.Warnings, w => w.Contains("Image widget", StringComparison.Ordinal));
    }

    [Fact]
    public void Overwriting_a_preset_made_before_the_image_widget_adds_it()
    {
        var (svc, store, _) = Create();
        // 사진 위젯이 생기기 전 버전이 저장한 프리셋 흉내 (스키마 1, 사진 위젯 없음)
        var old = new Preset { Name = "1st", SchemaVersion = 1, Theme = new ThemeSettings(), Widget = new WidgetLayout() };
        store.Save(old);

        svc.Overwrite(store.Get(old.Id)!);

        var saved = store.Get(old.Id)!;
        Assert.NotNull(saved.ImageWidget);
        Assert.NotNull(saved.ImageWidget!.ImagePath);
        Assert.Null(saved.Wallpaper); // 원래 없던 다른 항목은 여전히 안 넣는다
        Assert.Equal(Preset.CurrentSchemaVersion, saved.SchemaVersion);
    }

    [Fact]
    public void Overwriting_a_new_preset_saved_without_image_widget_keeps_it_out()
    {
        var (svc, store, _) = Create();
        var preset = svc.Capture("themes only", PresetParts.Theme);

        svc.Overwrite(store.Get(preset.Id)!);

        var saved = store.Get(preset.Id)!;
        Assert.Null(saved.ImageWidget);
        Assert.Equal(PresetParts.Theme, saved.AvailableParts);
    }

    [Fact]
    public void Old_presets_without_image_widget_leave_it_alone()
    {
        var (svc, _, host) = Create();
        var old = new Preset { Name = "old", Theme = new ThemeSettings() };

        var result = svc.Apply(old);

        Assert.False(result.Applied.HasFlag(PresetParts.ImageWidget));
        Assert.Empty(host.Applied);
    }
}
