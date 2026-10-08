using System.Text.Json.Nodes;
using MoniMS.Core.Shell;
using MoniMS.Core.Shell.Apps;

namespace MoniMS.Core.Tests;

/// <summary>
/// "dotfiles" 형태의 테마 저장소 (Discord 테마가 확장자 없이, Komorebi·YASB 설정이 폴더별로 들어 있음).
/// 예: NonameNguyeen/My-dotfiles.
/// </summary>
public sealed class DotfilesThemeTests : IDisposable
{
    private const string Midnight = "/**\n * @name midnight (background)\n * @author refact0r\n*/\n@import url('https://x/midnight.css');\nbody { --font: 'JetBrainsMono Nerd Font'; }";
    private const string System24 = "/**\n * @name system24 (catppuccin mocha)\n*/\nbody { --font: 'DM Mono'; }";

    private const string KomorebiSource = """
        {
          "$schema": "https://raw.githubusercontent.com/LGUG2Z/komorebi/v0.1.41/schema.json",
          "app_specific_configuration_path": "$Env:USERPROFILE/applications.json",
          "default_workspace_padding": 6,
          "border": true,
          "border_width": 3,
          "transparency": true,
          "animation": { "enabled": true, "duration": 300 },
          "theme": { "palette": "Catppuccin", "name": "Mocha" },
          "monitors": [ { "workspaces": [ { "name": "I", "layout": "BSP" } ] } ]
        }
        """;

    private const string MyKomorebi = """
        {
          "app_specific_configuration_path": "$Env:USERPROFILE/applications.json",
          "default_workspace_padding": 20,
          "theme": { "palette": "Base16", "name": "Ashes" },
          "monitors": [ { "workspaces": [ { "name": "code", "layout": "Columns" }, { "name": "web", "layout": "Rows" } ] } ],
          "float_override": true
        }
        """;

    private const string YasbConfig = "watch_stylesheet: true\nbars:\n  status-bar:\n    enabled: true\nwidgets:\n  clock:\n    type: \"yasb.clock.ClockWidget\"\n";
    private const string YasbStyle = ":root { --font-bar: \"JetBrainsMono NFP\"; --font-size: 12px; }\n.yasb-bar { font-family: var(--font-bar), 'Segoe UI', sans-serif; }";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "moniMS-dotfiles-" + Guid.NewGuid().ToString("N"));

    public DotfilesThemeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private string MakeDotfiles()
    {
        var dir = Path.Combine(_root, "My-dotfiles-main");
        void Write(string relative, string text)
        {
            var path = Path.Combine(dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        Write("Discord/Current Theme", Midnight);
        Write("Discord/System24 catppuccin mocha theme", System24);
        Write("Komorebi/komorebi.json", KomorebiSource);
        Write("Komorebi/whkd", ".shell powershell\nalt + q : komorebic close\n");
        Write("YASB/Config.yaml", YasbConfig);
        Write("YASB/Style.css", YasbStyle);
        Write("YASB/Okinami Theme/config.yaml", YasbConfig.Replace("ClockWidget", "OkinamiClock", StringComparison.Ordinal));
        Write("YASB/Okinami Theme/styles.css", ".yasb-bar { background: #000; }");
        Write("Cava/config", "[general]\nframerate = 60\n");
        return dir;
    }

    [Fact]
    public async Task Finds_extensionless_discord_themes_komorebi_and_yasb_with_choices()
    {
        var library = new ThemeLibrary(Path.Combine(_root, "lib"));
        var package = await new ThemeImporter(library).ImportAsync(MakeDotfiles());

        var discord = package.Apps.Single(a => a.Kind == AppThemeKind.Discord);
        Assert.Equal(["midnight (background)", "system24 (catppuccin mocha)"], discord.Variants.Select(v => v.Name));
        Assert.Equal(discord.Variants[0].SourceFile, discord.SourceFile);

        var komorebi = package.Apps.Single(a => a.Kind == AppThemeKind.Komorebi);
        Assert.Equal("theme 'Catppuccin Mocha'", komorebi.Summary);
        Assert.Empty(komorebi.Variants);

        var yasb = package.Apps.Single(a => a.Kind == AppThemeKind.Yasb);
        Assert.Equal(["YASB", "Okinami Theme"], yasb.Variants.Select(v => v.Name)); // 얕은 폴더(기본)가 먼저
        Assert.EndsWith("Config.yaml", yasb.SourceFile, StringComparison.Ordinal);

        Assert.Equal(["Cava"], package.OtherItems);
    }

    [Theory]
    [InlineData("Discord/Current Theme", Midnight, true)]
    [InlineData("Discord/notes", "just some notes", false)]
    [InlineData("Other/Current Theme", Midnight, false)]
    public void Accepts_extensionless_discord_theme_only_in_discord_folder_with_meta(string relative, string text, bool expected)
    {
        var file = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        Assert.Equal(expected, DiscordThemer.LooksLikeDiscordTheme(relative, file));
    }

    [Fact]
    public void Extensionless_discord_theme_gets_a_css_file_name_from_its_meta_name()
    {
        var file = Path.Combine(_root, "System24 catppuccin mocha theme");
        File.WriteAllText(file, System24);
        Assert.Equal(("system24-catppuccin-mocha.theme.css", "system24 (catppuccin mocha)"), DiscordThemer.Identify(file));

        var css = Path.Combine(_root, "cool.theme.css");
        File.WriteAllText(css, "body {}");
        Assert.Equal(("cool.theme.css", "cool"), DiscordThemer.Identify(css));
    }

    [Fact]
    public void Komorebi_merge_takes_only_the_look_and_keeps_my_workspaces()
    {
        var target = (JsonObject)JsonNode.Parse(MyKomorebi)!;
        KomorebiThemer.Merge(target, (JsonObject)JsonNode.Parse(KomorebiSource)!);

        Assert.Equal("Catppuccin", target["theme"]!["palette"]!.GetValue<string>());
        Assert.Equal(6, target["default_workspace_padding"]!.GetValue<int>());
        Assert.True(target["animation"]!["enabled"]!.GetValue<bool>());
        Assert.Equal(["code", "web"], target["monitors"]![0]!["workspaces"]!.AsArray().Select(w => w!["name"]!.GetValue<string>()));
        Assert.True(target["float_override"]!.GetValue<bool>());
    }

    [Fact]
    public void Komorebi_paths_expand_env_vars_against_the_target_profile()
    {
        var profile = Path.Combine(_root, "me");
        Assert.Equal(Path.Combine(profile, "applications.json"), KomorebiThemer.ExpandPath("$Env:USERPROFILE/applications.json", profile));
        Assert.Equal(Path.Combine(profile, "applications.json"), KomorebiThemer.ExpandPath("$KOMOREBI_CONFIG_HOME/applications.json", profile));
        Assert.Equal(Path.Combine(profile, "x.json"), KomorebiThemer.ExpandPath("~/x.json", profile));
    }

    [Fact]
    public void Css_fonts_skip_generic_families_variables_and_sizes()
    {
        var file = Path.Combine(_root, "styles.css");
        File.WriteAllText(file, YasbStyle);
        Assert.Equal(["JetBrainsMono NFP", "Segoe UI"], CssFonts.Read(file));
    }

    [Fact]
    public async Task Applies_chosen_themes_switches_discord_theme_and_restores_everything()
    {
        var profile = Path.Combine(_root, "Profile");
        var roaming = Path.Combine(_root, "Roaming");

        // Vencord: 내가 직접 넣은 테마 하나가 켜져 있음
        var vencordSettings = Path.Combine(roaming, "Vencord", "settings", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(vencordSettings)!);
        const string vencordOriginal = """{ "enabledThemes": ["mine.css"] }""";
        File.WriteAllText(vencordSettings, vencordOriginal);

        // Komorebi: 내 설정 있음, whkdrc 없음 / YASB: 기본 설정 있음
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(profile).FullName, "komorebi.json"), MyKomorebi);
        var yasbDir = Directory.CreateDirectory(Path.Combine(profile, ".config", "yasb")).FullName;
        File.WriteAllText(Path.Combine(yasbDir, "config.yaml"), "my config");
        File.WriteAllText(Path.Combine(yasbDir, "styles.css"), "my styles");

        var library = new ThemeLibrary(Path.Combine(_root, "lib"));
        var package = await new ThemeImporter(library).ImportAsync(MakeDotfiles());
        var storage = new ShellThemeTests.FakeStorage();
        var applier = new ThemeApplier(storage, new ShellThemeTests.DirectWriter(storage), new ShellThemeTests.NoWallpaper(), library,
            targets: new AppThemeTargets(Path.Combine(_root, "Local"), roaming, _ => true, profile));

        var discord = package.Apps.Single(a => a.Kind == AppThemeKind.Discord);
        var yasb = package.Apps.Single(a => a.Kind == AppThemeKind.Yasb);
        var all = new[] { AppThemeKind.Discord, AppThemeKind.Komorebi, AppThemeKind.Yasb };

        // 1) system24 + Okinami 선택
        var first = applier.Apply(package, new ThemeApplyOptions([], false, null, all, new Dictionary<AppThemeKind, string>
        {
            [AppThemeKind.Discord] = discord.Variants[1].SourceFile,
            [AppThemeKind.Yasb] = yasb.Variants[1].SourceFile,
        }));
        Assert.Equal(["Discord (Vencord)", "Komorebi", "YASB (Okinami Theme)"], first.Apps);
        Assert.DoesNotContain(first.Warnings, w => w.Contains("applications.json", StringComparison.Ordinal)); // 내 설정에 합쳤으니 경로는 내 것

        var themes = Path.Combine(roaming, "Vencord", "themes");
        Assert.True(File.Exists(Path.Combine(themes, "system24-catppuccin-mocha.theme.css")));
        Assert.Equal(["mine.css", "system24-catppuccin-mocha.theme.css"], EnabledThemes(vencordSettings));

        var komorebi = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(profile, "komorebi.json")))!;
        Assert.Equal("Mocha", komorebi["theme"]!["name"]!.GetValue<string>());
        Assert.Equal(2, komorebi["monitors"]![0]!["workspaces"]!.AsArray().Count); // 내 작업 공간 유지
        var whkdrc = Path.Combine(profile, ".config", "whkdrc");
        Assert.Contains("komorebic close", File.ReadAllText(whkdrc), StringComparison.Ordinal); // 없어서 넣음

        Assert.Contains("OkinamiClock", File.ReadAllText(Path.Combine(yasbDir, "config.yaml")), StringComparison.Ordinal);
        Assert.Equal(".yasb-bar { background: #000; }", File.ReadAllText(Path.Combine(yasbDir, "styles.css")));

        // 2) 기본(midnight)으로 바꾸면 예전 system24는 꺼지고 내 테마는 그대로
        Thread.Sleep(5);
        applier.Apply(package, new ThemeApplyOptions([], false, null, [AppThemeKind.Discord]));
        Assert.Equal(["mine.css", "midnight-background.theme.css"], EnabledThemes(vencordSettings));

        // 3) Reset: 처음 상태 그대로
        var restored = applier.ResetToOriginal();
        Assert.Equal(["Discord", "Komorebi", "YASB"], restored.Order());
        Assert.Equal(vencordOriginal, File.ReadAllText(vencordSettings));
        Assert.Equal(MyKomorebi, File.ReadAllText(Path.Combine(profile, "komorebi.json")));
        Assert.False(File.Exists(whkdrc));
        Assert.Equal("my config", File.ReadAllText(Path.Combine(yasbDir, "config.yaml")));
        Assert.Equal("my styles", File.ReadAllText(Path.Combine(yasbDir, "styles.css")));
        Assert.Empty(Directory.GetFiles(themes));
    }

    [Fact]
    public void Komorebi_without_my_config_uses_the_theme_file_and_points_to_missing_applications_json()
    {
        var source = Path.Combine(_root, "theme", "komorebi.json");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, KomorebiSource);
        var profile = Path.Combine(_root, "fresh");
        var journal = new FileJournal();
        var warnings = new List<string>();

        Assert.Equal(1, KomorebiThemer.Apply(source, journal, warnings, profile));

        Assert.Equal(KomorebiSource, File.ReadAllText(Path.Combine(profile, "komorebi.json")));
        Assert.Null(journal.Originals[Path.Combine(profile, "komorebi.json")]); // 복원하면 지워짐
        Assert.Contains(warnings, w => w.Contains("fetch-app-specific-configuration", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Komorebi_and_yasb_are_skipped_when_not_installed()
    {
        var library = new ThemeLibrary(Path.Combine(_root, "lib"));
        var package = await new ThemeImporter(library).ImportAsync(MakeDotfiles());
        var storage = new ShellThemeTests.FakeStorage();
        var applier = new ThemeApplier(storage, new ShellThemeTests.DirectWriter(storage), new ShellThemeTests.NoWallpaper(), library,
            targets: new AppThemeTargets(Path.Combine(_root, "L"), Path.Combine(_root, "R"), _ => true, Path.Combine(_root, "empty-profile")));

        var result = applier.Apply(package, new ThemeApplyOptions([], false, null, [AppThemeKind.Komorebi, AppThemeKind.Yasb]));

        Assert.Empty(result.Apps);
        Assert.Contains("Komorebi is not installed, skipped.", result.Warnings);
        Assert.Contains("YASB is not installed, skipped.", result.Warnings);
        Assert.False(applier.HasBackup);
    }

    private static IEnumerable<string> EnabledThemes(string vencordSettings) =>
        ((JsonObject)JsonNode.Parse(File.ReadAllText(vencordSettings))!)["enabledThemes"]!.AsArray().Select(e => e!.GetValue<string>());
}
