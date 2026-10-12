using System.Text.Json;
using MoniMS.Core.Desktop;
using MoniMS.Core.Files;
using MoniMS.Core.Presets;
using MoniMS.Core.Settings;

namespace MoniMS.Core.Tests;

public sealed class FileWidgetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "moniMS-files-" + Guid.NewGuid().ToString("N"));

    public FileWidgetTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (!Directory.Exists(_root))
            return;
        foreach (var f in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal); // 숨김·시스템 파일도 지울 수 있게
        Directory.Delete(_root, true);
    }

    private static FileEntry File_(string name) => new(@"C:\x\" + name, isFolder: false, FileAttributes.Normal);

    private static FileEntry Folder(string name) => new(@"C:\x\" + name, isFolder: true, FileAttributes.Directory);

    // ---------- 이름 / 확장자 ----------

    [Theory]
    [InlineData("report.pdf", false, "report", "pdf")]
    [InlineData("Photo.JPG", false, "Photo", "jpg")]
    [InlineData("archive.tar.gz", false, "archive.tar", "gz")]
    [InlineData("README", false, "README", "")]
    [InlineData(".gitignore", false, ".gitignore", "")]
    [InlineData("odd.", false, "odd.", "")]
    [InlineData("my.folder", true, "my.folder", "")]
    public void Splits_names_into_name_and_extension_columns(string name, bool isFolder, string baseName, string extension) =>
        Assert.Equal((baseName, extension), FolderReader.SplitName(name, isFolder));

    [Theory]
    [InlineData(FileAttributes.Normal, false, false, true)]
    [InlineData(FileAttributes.System, false, false, true)] // 시스템 속성만 있으면 보인다
    [InlineData(FileAttributes.Hidden, false, false, false)]
    [InlineData(FileAttributes.Hidden, true, false, true)]
    [InlineData(FileAttributes.Hidden | FileAttributes.System, true, false, false)] // 보호된 OS 파일
    [InlineData(FileAttributes.Hidden | FileAttributes.System, true, true, true)]
    public void Hidden_items_follow_the_file_explorer_settings(FileAttributes attributes, bool showHidden, bool showProtected, bool expected) =>
        Assert.Equal(expected, FolderReader.IsShown(attributes, showHidden, showProtected));

    [Fact]
    public void Lists_files_and_folders_without_hidden_ones()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "a.txt"), "a");
        var hidden = Path.Combine(_root, "secret.txt");
        File.WriteAllText(hidden, "s");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        var desktopIni = Path.Combine(_root, "desktop.ini");
        File.WriteAllText(desktopIni, "[.ShellClassInfo]");
        File.SetAttributes(desktopIni, FileAttributes.Hidden | FileAttributes.System);

        var names = FolderReader.List(_root, showHidden: false, showProtected: false).Select(e => (e.Name, e.IsFolder)).Order().ToList();
        Assert.Equal([("a.txt", false), ("sub", true)], names);

        var withHidden = FolderReader.List(_root, showHidden: true, showProtected: false).Select(e => e.Name).Order().ToList();
        Assert.Equal(["a.txt", "secret.txt", "sub"], withHidden);
        Assert.True(FolderReader.List(_root, true, false).Single(e => e.Name == "secret.txt").IsHidden);
    }

    [Fact]
    public void Missing_folder_throws_so_the_widget_can_say_why()
    {
        Assert.Throws<DirectoryNotFoundException>(() => FolderReader.List(Path.Combine(_root, "nope"), false, false));
    }

    // ---------- 정렬 ----------

    [Fact]
    public void Sorts_folders_first_and_numbers_by_value_like_file_explorer()
    {
        FileEntry[] entries = [File_("file10.txt"), Folder("zeta"), File_("File2.txt"), File_("b.md"), Folder("Alpha")];

        var byName = FolderReader.Sort(entries, FileSortColumn.Name, descending: false).Select(e => e.Name);
        Assert.Equal(["Alpha", "zeta", "b.md", "File2.txt", "file10.txt"], byName);

        var byNameDesc = FolderReader.Sort(entries, FileSortColumn.Name, descending: true).Select(e => e.Name);
        Assert.Equal(["zeta", "Alpha", "file10.txt", "File2.txt", "b.md"], byNameDesc); // 폴더는 여전히 위
    }

    [Fact]
    public void Sorts_by_extension_then_name()
    {
        FileEntry[] entries = [File_("b.txt"), File_("a.txt"), File_("z.md"), File_("noext"), Folder("dir")];

        var byExt = FolderReader.Sort(entries, FileSortColumn.Extension, descending: false).Select(e => e.Name);

        Assert.Equal(["dir", "noext", "z.md", "a.txt", "b.txt"], byExt);
    }

    [Fact]
    public void Sort_order_is_total_even_when_names_compare_equal()
    {
        FileEntry[] entries = [File_("file1.txt"), File_("file01.txt")];
        var a = FolderReader.Sort(entries, FileSortColumn.Name, false).Select(e => e.Name).ToList();
        var b = FolderReader.Sort(entries.Reverse(), FileSortColumn.Name, false).Select(e => e.Name).ToList();
        Assert.Equal(a, b);
    }

    // ---------- 끌어 놓기 / 붙여넣기 ----------

    [Theory]
    [InlineData(@"C:\a\x.txt", @"C:\b", false, false, TransferAction.Move)] // 같은 드라이브 = 이동
    [InlineData(@"D:\a\x.txt", @"C:\b", false, false, TransferAction.Copy)] // 다른 드라이브 = 복사
    [InlineData(@"C:\a\x.txt", @"C:\b", true, false, TransferAction.Copy)] // Ctrl = 복사
    [InlineData(@"D:\a\x.txt", @"C:\b", false, true, TransferAction.Move)] // Shift = 이동
    [InlineData(@"C:\a\x.txt", @"C:\a", false, false, TransferAction.None)] // 원래 폴더에 놓기
    [InlineData(@"C:\a\x.txt", @"C:\a\", true, false, TransferAction.Copy)] // 원래 폴더에 Ctrl = 복사본
    [InlineData(@"C:\a", @"C:\a\sub", false, false, TransferAction.None)] // 자기 안으로
    [InlineData(@"C:\a", @"C:\a", true, false, TransferAction.None)]
    [InlineData(@"C:\a", @"C:\ab", false, false, TransferAction.Move)] // 이름만 비슷한 다른 폴더
    public void Drop_follows_file_explorer_rules(string source, string target, bool ctrl, bool shift, TransferAction expected) =>
        Assert.Equal(expected, FileTransferRules.ForDrop([source], target, ctrl, shift));

    [Theory]
    [InlineData(@"C:\a\x.txt", @"C:\b", false, TransferAction.Copy)]
    [InlineData(@"C:\a\x.txt", @"C:\b", true, TransferAction.Move)]
    [InlineData(@"C:\a\x.txt", @"C:\a", false, TransferAction.Copy)] // 같은 폴더에 복사본
    [InlineData(@"C:\a\x.txt", @"C:\a", true, TransferAction.None)]
    [InlineData(@"C:\a", @"C:\a\sub", false, TransferAction.None)]
    public void Paste_copies_or_moves_what_was_cut(string source, string target, bool cut, TransferAction expected) =>
        Assert.Equal(expected, FileTransferRules.ForPaste([source], target, cut));

    [Theory]
    [InlineData(@"C:\Users\me\Docs\a", @"C:\Users\me\Docs", true)]
    [InlineData(@"C:\Users\me\Docs", @"C:\Users\me\Docs\", true)]
    [InlineData(@"C:\Users\me\Docs2", @"C:\Users\me\Docs", false)]
    [InlineData(@"D:\x", @"D:\", true)]
    public void Knows_which_folder_a_path_is_in(string path, string folder, bool expected) =>
        Assert.Equal(expected, FileTransferRules.IsInside(path, folder));

    // ---------- 새 폴더 / 이름 바꾸기 ----------

    [Fact]
    public void New_folder_names_count_up_like_file_explorer()
    {
        Assert.Equal("New folder", FolderReader.UniqueName(_root, "New folder"));
        Directory.CreateDirectory(Path.Combine(_root, "New folder"));
        Assert.Equal("New folder (2)", FolderReader.UniqueName(_root, "New folder"));
        File.WriteAllText(Path.Combine(_root, "New folder (2)"), ""); // 같은 이름의 파일도 겹친다
        Assert.Equal("New folder (3)", FolderReader.UniqueName(_root, "New folder"));
    }

    [Theory]
    [InlineData("notes.txt", null, "notes.txt")]
    [InlineData("  spaced name.txt  ", null, "spaced name.txt")]
    [InlineData("trailing dot.", null, "trailing dot")]
    [InlineData("", "type", "")]
    [InlineData("   ", "type", "")]
    [InlineData("a:b.txt", "characters", "a:b.txt")]
    [InlineData("what?.txt", "characters", "what?.txt")]
    [InlineData("CON", "reserved", "CON")]
    [InlineData("nul.txt", "reserved", "nul.txt")]
    [InlineData("console.txt", null, "console.txt")]
    public void Validates_new_names(string input, string? errorContains, string cleaned)
    {
        var error = FolderReader.ValidateName(input, out var name);
        if (errorContains is null)
            Assert.Null(error);
        else
            Assert.Contains(errorContains, error, StringComparison.OrdinalIgnoreCase);
        if (error is null || cleaned.Length > 0)
            Assert.Equal(cleaned, name);
    }

    // ---------- 레이아웃 / 설정 ----------

    [Fact]
    public void Clone_copies_the_folder_list()
    {
        var layout = new FileWidgetLayout { Folders = [@"C:\a"] };
        var copy = layout.Clone();
        copy.Folders.Add(@"C:\b");
        Assert.Single(layout.Folders);
    }

    [Fact]
    public void Settings_round_trip_keeps_folders_and_sort()
    {
        var settings = new AppSettings
        {
            FileWidget = { Folders = [@"C:\Users\me\Documents", @"D:\Projects"], SortBy = FileSortColumn.Extension, SortDescending = true },
        };
        var json = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options)!;

        Assert.Equal(settings.FileWidget.Folders, loaded.FileWidget.Folders);
        Assert.Equal(FileSortColumn.Extension, loaded.FileWidget.SortBy);
        Assert.True(loaded.FileWidget.SortDescending);
        Assert.DoesNotContain("hasFolders", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Settings_from_before_the_file_widget_start_without_folders()
    {
        var loaded = JsonSerializer.Deserialize<AppSettings>("""{ "version": 5 }""", JsonDefaults.Options)!;
        Assert.False(loaded.FileWidget.HasFolders); // 창이 갑자기 뜨지 않는다
    }

    // ---------- 프리셋 ----------

    private sealed class FakeFileHost(string folder) : IFileWidgetHost
    {
        public FileWidgetLayout Current { get; set; } = new() { Folders = [folder], Left = 10, Top = 20, Width = 500, Height = 300 };

        public List<FileWidgetLayout> Applied { get; } = [];

        public FileWidgetLayout GetCurrentLayout() => Current;

        public void ApplyLayout(FileWidgetLayout layout) => Applied.Add(layout);
    }

    private sealed class NoWallpaper : IWallpaperService
    {
        public WallpaperSettings Capture(string dir) => new();
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

    private (PresetService Service, PresetStore Store, FakeFileHost Host) Create()
    {
        var store = new PresetStore(Path.Combine(_root, "presets"));
        var host = new FakeFileHost(_root);
        return (new PresetService(store, new NoWallpaper(), new NoTheme(), new NoIcons(), new NoWidget(), fileWidget: host), store, host);
    }

    [Fact]
    public void Preset_saves_and_restores_the_file_widget()
    {
        var (svc, store, host) = Create();
        var preset = svc.Capture("x");
        host.Current.Folders.Add(@"C:\changed later"); // 저장한 뒤 바뀌어도 프리셋은 그대로

        var result = svc.Apply(store.Get(preset.Id)!);

        Assert.True(result.Applied.HasFlag(PresetParts.FileWidget));
        Assert.Empty(result.Warnings);
        var applied = Assert.Single(host.Applied);
        Assert.Equal([_root], applied.Folders);
        Assert.Equal((10d, 20d, 500d, 300d), (applied.Left, applied.Top, applied.Width, applied.Height));
    }

    [Fact]
    public void Missing_folders_stay_in_the_list_with_a_warning()
    {
        var (svc, store, host) = Create();
        var gone = Path.Combine(_root, "usb");
        host.Current.Folders = [_root, gone];
        var preset = svc.Capture("x", PresetParts.FileWidget);

        var result = svc.Apply(store.Get(preset.Id)!);

        Assert.Equal([_root, gone], Assert.Single(host.Applied).Folders);
        Assert.Contains(result.Warnings, w => w.Contains("File widget", StringComparison.Ordinal));
    }

    [Fact]
    public void Overwriting_a_preset_made_before_the_file_widget_adds_it()
    {
        var (svc, store, _) = Create();
        var old = new Preset { Name = "v2", SchemaVersion = 2, Theme = new ThemeSettings() };
        store.Save(old);

        svc.Overwrite(store.Get(old.Id)!);

        var saved = store.Get(old.Id)!;
        Assert.Equal([_root], saved.FileWidget!.Folders);
        Assert.Null(saved.ImageWidget); // 사진 위젯은 스키마 2에서 이미 고를 수 있었다
        Assert.Equal(Preset.CurrentSchemaVersion, saved.SchemaVersion);
    }

    [Fact]
    public void Overwriting_a_new_preset_saved_without_file_widget_keeps_it_out()
    {
        var (svc, store, _) = Create();
        var preset = svc.Capture("theme only", PresetParts.Theme);

        svc.Overwrite(store.Get(preset.Id)!);

        Assert.Null(store.Get(preset.Id)!.FileWidget);
    }
}
