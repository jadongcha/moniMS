using System.IO.Compression;
using System.Text;
using MoniMS.Core.Desktop;
using MoniMS.Core.Presets;
using MoniMS.Core.Shell;

namespace MoniMS.Core.Tests;

public sealed class ShellThemeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "moniMS-shell-" + Guid.NewGuid().ToString("N"));

    public ShellThemeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    // ---------- 주소 해석 ----------

    [Theory]
    [InlineData("https://github.com/MrDLingters/Win11GruvboxMaterial", ThemeSourceKind.RemoteZip,
        "https://api.github.com/repos/MrDLingters/Win11GruvboxMaterial/zipball", "Win11GruvboxMaterial", null)]
    [InlineData("github.com/owner/repo.git", ThemeSourceKind.RemoteZip,
        "https://api.github.com/repos/owner/repo/zipball", "repo", null)]
    [InlineData("https://github.com/o/r/tree/dev/themes/blue", ThemeSourceKind.RemoteZip,
        "https://codeload.github.com/o/r/zip/refs/heads/dev", "r", "themes/blue")]
    [InlineData("https://github.com/o/r/blob/main/Windhawk/Taskbar.json", ThemeSourceKind.RemoteFile,
        "https://raw.githubusercontent.com/o/r/main/Windhawk/Taskbar.json", "Taskbar", null)]
    [InlineData("https://example.com/files/cool-theme.zip", ThemeSourceKind.RemoteZip,
        "https://example.com/files/cool-theme.zip", "cool-theme", null)]
    public void Parses_links(string input, ThemeSourceKind kind, string location, string name, string? sub)
    {
        var s = ThemeSource.Parse(input);
        Assert.Equal(kind, s.Kind);
        Assert.Equal(location, s.Location);
        Assert.Equal(name, s.SuggestedName);
        Assert.Equal(sub, s.SubPath);
    }

    [Fact]
    public void Missing_local_path_is_an_error() =>
        Assert.Throws<FileNotFoundException>(() => ThemeSource.Parse(Path.Combine(_root, "nope.zip")));

    // ---------- 모드 판별 ----------

    [Fact]
    public void Detects_mods_by_file_name_and_content()
    {
        var taskbar = new Dictionary<string, object> { ["controlStyles[0].target"] = "Taskbar.TaskbarFrame", ["theme"] = "" };
        Assert.Equal(WindhawkMods.TaskbarStyler, ThemeScanner.DetectMod("Taskbar.json", taskbar));
        Assert.Equal(WindhawkMods.StartMenuStyler, ThemeScanner.DetectMod("StartMenu.json", taskbar));
        Assert.Equal(WindhawkMods.NotificationCenterStyler, ThemeScanner.DetectMod("NotificationCenter.json", taskbar));
        // 이름으로 모르면 target 내용으로
        Assert.Equal(WindhawkMods.TaskbarStyler, ThemeScanner.DetectMod("styles.json", taskbar));
        var start = new Dictionary<string, object> { ["controlStyles[0].target"] = "StartDocked.LauncherFrame > Grid" };
        Assert.Equal(WindhawkMods.StartMenuStyler, ThemeScanner.DetectMod("a.json", start));
        var icons = new Dictionary<string, object> { ["iconTheme"] = "x", ["allResourceRedirect"] = 0 };
        Assert.Equal(WindhawkMods.ResourceRedirect, ThemeScanner.DetectMod("ResourceRedirect.json", icons));
        Assert.Null(ThemeScanner.DetectMod("package.json", new Dictionary<string, object> { ["name"] = "x" }));
    }

    [Fact]
    public void Nested_json_is_not_a_windhawk_config()
    {
        var file = Path.Combine(_root, "settings.json");
        File.WriteAllText(file, """{ "profiles": { "list": [] }, "theme": "dark" }""");
        Assert.Null(ThemeScanner.TryReadFlatSettings(file));
    }

    [Fact]
    public void Custom_script_is_detected_and_can_be_stripped()
    {
        var s = new Dictionary<string, object> { ["webContentCustomJs"] = "alert(1)", ["theme"] = "" };
        Assert.True(ThemeScanner.HasCustomScript(s));
        var stripped = ThemeScanner.WithoutCustomScript(s);
        Assert.Equal("", stripped["webContentCustomJs"]);
        Assert.False(ThemeScanner.HasCustomScript(stripped));
    }

    // ---------- 가져오기 (로컬 zip) ----------

    private string MakeSampleZip()
    {
        var zipPath = Path.Combine(_root, "Win11Sample-main.zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
            w.Write(content);
        }
        Add("Win11Sample-main/README.md", "# Sample theme for Windows 11\nbody");
        Add("Win11Sample-main/Windhawk/Taskbar.json", """{ "controlStyles[0].target": "Taskbar.TaskbarFrame", "controlStyles[0].styles[0]": "Fill:=#32302F", "theme": "" }""");
        Add("Win11Sample-main/Windhawk/StartMenu.json", """{ "theme": "", "disableNewStartMenuLayout": 0, "webContentCustomJs": "" }""");
        Add("Win11Sample-main/Windhawk/ResourceRedirect.json", """{ "iconTheme": "", "allResourceRedirect": 0 }""");
        Add("Win11Sample-main/Terminal/settings.json", """{ "profiles": { "list": [] } }""");
        Add("Win11Sample-main/Wallpapers/a.png", "png");
        Add("Win11Sample-main/Wallpapers/b.jpg", "jpg");
        return zipPath;
    }

    [Fact]
    public async Task Imports_local_zip_and_finds_everything()
    {
        var library = new ThemeLibrary(Path.Combine(_root, "lib"));
        var importer = new ThemeImporter(library);

        var p = await importer.ImportAsync(MakeSampleZip());

        Assert.Equal("Win11Sample", p.Name);
        Assert.Equal("Sample theme for Windows 11", p.Description);
        Assert.Equal(
            new[] { WindhawkMods.ResourceRedirect, WindhawkMods.StartMenuStyler, WindhawkMods.TaskbarStyler }.Order(),
            p.Mods.Select(m => m.ModId).Order());
        Assert.Equal(2, p.Wallpapers.Count);
        Assert.Contains("Terminal", p.OtherItems);
        Assert.DoesNotContain("Windhawk", p.OtherItems);

        // 보관함에서 다시 읽힘
        var again = Assert.Single(library.GetAll());
        Assert.Equal(p.Id, again.Id);
        Assert.Equal(3, again.Mods.Count);
    }

    [Fact]
    public void Zip_slip_is_rejected()
    {
        var zipPath = Path.Combine(_root, "evil.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("../../evil.txt").Open()))
            w.Write("x");
        Assert.ThrowsAny<Exception>(() => ThemeImporter.SafeExtract(zipPath, Path.Combine(_root, "out")));
        Assert.False(File.Exists(Path.Combine(_root, "..", "evil.txt")));
    }

    [Fact]
    public async Task Package_without_any_theme_content_fails_and_cleans_up()
    {
        var library = new ThemeLibrary(Path.Combine(_root, "lib"));
        var dir = Path.Combine(_root, "empty");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "readme.txt"), "hi");
        await Assert.ThrowsAsync<InvalidDataException>(() => new ThemeImporter(library).ImportAsync(dir));
        Assert.Empty(Directory.GetDirectories(library.RootDirectory));
    }

    // ---------- 적용 / 백업 / 복원 ----------

    private sealed class FakeStorage : IWindhawkStorage
    {
        public Dictionary<string, Dictionary<string, object>> Mods { get; } = new()
        {
            [WindhawkMods.TaskbarStyler] = new() { ["old"] = "1" },
            [WindhawkMods.StartMenuStyler] = new(),
        };

        public string? CustomAppRoot { get; set; }
        public WindhawkInfo Detect() => new(true, "C:\\wh", false, "HKLM\\SOFTWARE\\Windhawk", null, "1.6");
        public ModState GetModState(string modId) => Mods.ContainsKey(modId) ? ModState.Enabled : ModState.NotInstalled;
        public IReadOnlyDictionary<string, object> ReadSettings(string modId) => new Dictionary<string, object>(Mods[modId]);
        public void WriteSettings(string modId, IReadOnlyDictionary<string, object> settings) => Mods[modId] = new(settings);
    }

    private sealed class DirectWriter(IWindhawkStorage s) : IWindhawkSettingsWriter
    {
        public void Write(IReadOnlyList<ModSettingsWrite> items)
        {
            foreach (var i in items)
                s.WriteSettings(i.ModId, i.Settings);
        }
    }

    private sealed class NoWallpaper : IWallpaperService
    {
        public WallpaperSettings Capture(string d) => new();
        public void Apply(WallpaperSettings s, string d) { }
        public void SetForAllMonitors(string p, WallpaperPosition pos) { }
    }

    [Fact]
    public async Task Apply_writes_installed_mods_backs_up_and_restores()
    {
        var library = new ThemeLibrary(Path.Combine(_root, "lib"));
        var p = await new ThemeImporter(library).ImportAsync(MakeSampleZip());
        var storage = new FakeStorage();
        var applier = new ThemeApplier(storage, new DirectWriter(storage), new NoWallpaper(), library);

        var result = applier.ApplyMods(p, new ThemeApplyOptions(p.Mods.Select(m => m.ModId).ToList(), false, null));

        Assert.Equal(new[] { WindhawkMods.StartMenuStyler, WindhawkMods.TaskbarStyler }.Order(), result.AppliedMods.Order());
        Assert.Contains(result.Warnings, w => w.Contains("not installed")); // Resource Redirect 미설치
        Assert.Equal("Fill:=#32302F", storage.Mods[WindhawkMods.TaskbarStyler]["controlStyles[0].styles[0]"]);
        Assert.Equal(0, storage.Mods[WindhawkMods.StartMenuStyler]["disableNewStartMenuLayout"]); // 정수 유지
        Assert.True(applier.HasBackup);

        applier.RestoreLatest();
        Assert.Equal("1", storage.Mods[WindhawkMods.TaskbarStyler]["old"]);
        Assert.Single(storage.Mods[WindhawkMods.TaskbarStyler]);
        Assert.False(applier.HasBackup);
    }

    [Fact]
    public void Elevated_request_roundtrip_and_rejects_unknown_mods()
    {
        var items = new List<ModSettingsWrite>
        {
            new(WindhawkMods.TaskbarStyler, new Dictionary<string, object> { ["a"] = "x", ["b"] = 3 }),
        };
        var back = ThemeApplier.DeserializeWrites(ThemeApplier.SerializeWrites(items));
        var w = Assert.Single(back);
        Assert.Equal("x", w.Settings["a"]);
        Assert.Equal(3, w.Settings["b"]);

        Assert.Throws<InvalidDataException>(() => ThemeApplier.DeserializeWrites("""{ "..\\..\\evil": {} }"""));
    }

    // ---------- windhawk.ini ----------

    [Fact]
    public void Parses_windhawk_ini_utf16()
    {
        var text = "[Storage]\r\nPortable=0\r\nAppDataPath=%ProgramData%\\Windhawk\r\nRegistryKey=HKEY_LOCAL_MACHINE\\SOFTWARE\\Windhawk\r\n[Other]\r\nPortable=1\r\n";
        var bytes = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(text)).ToArray();
        var s = WindhawkStorage.ParseStorageSection(bytes);
        Assert.Equal("0", s["Portable"]);
        Assert.Equal(@"HKEY_LOCAL_MACHINE\SOFTWARE\Windhawk", s["RegistryKey"]);
        Assert.Equal(1, WindhawkStorage.LeadingInt("1abc"));
        Assert.Equal(0, WindhawkStorage.LeadingInt("x"));
    }

    // ---------- 실제 패키지 (환경 변수로 지정 시) ----------

    [Fact]
    public async Task Real_sample_package_if_available()
    {
        var zip = Environment.GetEnvironmentVariable("MONIMS_SAMPLE_ZIP");
        if (string.IsNullOrEmpty(zip) || !File.Exists(zip))
            return;
        var library = new ThemeLibrary(Path.Combine(_root, "lib"));
        var p = await new ThemeImporter(library).ImportAsync(zip);
        Console.WriteLine($"name={p.Name} desc={p.Description}");
        foreach (var m in p.Mods)
            Console.WriteLine($"  mod {m.ModId} <- {m.SourceFile} ({m.SettingCount} settings, script={m.HasCustomScript})");
        Console.WriteLine($"  wallpapers={p.Wallpapers.Count} other=[{string.Join(", ", p.OtherItems)}]");
        Assert.Equal(4, p.Mods.Count);
    }
}
