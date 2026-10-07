using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MoniMS.Core.Shell.Apps;

public enum DiscordClientKind
{
    /// <summary>Vencord, Vesktop, Equicord, Equibop: themes\ 폴더 + settings\settings.json 의 enabledThemes.</summary>
    VencordFamily,

    /// <summary>BetterDiscord: themes\ 폴더 + data\{채널}\themes.json 의 { "테마 이름": true }.</summary>
    BetterDiscord,
}

public sealed record DiscordClient(string Name, string DataDirectory, DiscordClientKind Kind);

/// <summary>
/// Discord 테마(.theme.css) 적용. 순정 Discord는 테마를 지원하지 않아서,
/// 사용자가 이미 설치한 클라이언트 모드(Vencord / BetterDiscord 등)의 테마 폴더에 넣고 켜 준다.
/// 클라이언트 모드 자체를 설치하지는 않는다.
/// </summary>
public static partial class DiscordThemer
{
    private static readonly (string Folder, string Name, DiscordClientKind Kind)[] Known =
    [
        ("Vencord", "Vencord", DiscordClientKind.VencordFamily),
        ("vesktop", "Vesktop", DiscordClientKind.VencordFamily),
        ("Equicord", "Equicord", DiscordClientKind.VencordFamily),
        ("equibop", "Equibop", DiscordClientKind.VencordFamily),
        ("BetterDiscord", "BetterDiscord", DiscordClientKind.BetterDiscord),
    ];

    private static readonly string[] DiscordProcesses = ["Discord", "DiscordPTB", "DiscordCanary", "Vesktop", "vesktop", "Equibop", "equibop"];

    public static IReadOnlyList<DiscordClient> FindClients(string? appData = null)
    {
        appData ??= Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Known
            .Select(k => new DiscordClient(k.Name, Path.Combine(appData, k.Folder), k.Kind))
            .Where(c => Directory.Exists(c.DataDirectory))
            .ToList();
    }

    public static bool IsDiscordRunning() =>
        DiscordProcesses.Any(n =>
        {
            var ps = Process.GetProcessesByName(n);
            var running = ps.Length > 0;
            foreach (var p in ps) p.Dispose();
            return running;
        });

    /// <summary>테마 파일인지 판별: .theme.css 이거나, discord 폴더 안의 css이거나, @name 메타가 있는 css.</summary>
    public static bool LooksLikeDiscordTheme(string relativePath, string file)
    {
        if (!file.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            return false;
        if (file.EndsWith(".theme.css", StringComparison.OrdinalIgnoreCase) ||
            relativePath.Contains("discord", StringComparison.OrdinalIgnoreCase))
            return true;
        return ReadThemeName(file) is not null && relativePath.Contains("vencord", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>테마 머리말의 "@name ..." (BetterDiscord는 이 이름으로 켜짐 상태를 저장).</summary>
    public static string? ReadThemeName(string file)
    {
        try
        {
            using var reader = new StreamReader(file);
            var head = new char[4096];
            var n = reader.Read(head, 0, head.Length);
            var m = NameRegex().Match(new string(head, 0, n));
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"@name\s+([^\r\n*]+)")]
    private static partial Regex NameRegex();

    /// <summary>테마가 쓰는 글꼴 (--font: 'DM Mono' 같은 CSS 변수).</summary>
    public static IReadOnlyList<string> ReadFonts(string file)
    {
        try
        {
            return FontRegex().Matches(File.ReadAllText(file))
                .Select(m => m.Groups[1].Value.Trim())
                .Where(f => f.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
    }

    [GeneratedRegex(@"--(?:code-)?font\s*:\s*['""]([^'""]*)['""]")]
    private static partial Regex FontRegex();

    /// <summary>찾은 모든 클라이언트에 테마를 넣고 켠다. 적용한 클라이언트 이름 목록 반환.</summary>
    public static IReadOnlyList<string> Apply(string sourceCss, FileJournal journal, List<string> warnings,
        string? appData = null, Func<string, bool>? fontInstalled = null)
    {
        var clients = FindClients(appData);
        if (clients.Count == 0)
        {
            warnings.Add("Discord: no theme-capable client found (Vencord, Vesktop, Equicord or BetterDiscord), skipped.");
            return [];
        }

        var fileName = Path.GetFileName(sourceCss);
        var themeName = ReadThemeName(sourceCss) ?? Path.GetFileNameWithoutExtension(fileName).Replace(".theme", "", StringComparison.OrdinalIgnoreCase);
        var applied = new List<string>();

        foreach (var client in clients)
        {
            var themesDir = Path.Combine(client.DataDirectory, "themes");
            Directory.CreateDirectory(themesDir);
            var dest = Path.Combine(themesDir, fileName);
            journal.Record(dest);
            File.Copy(sourceCss, dest, overwrite: true);

            if (client.Kind == DiscordClientKind.VencordFamily)
            {
                var settingsPath = Path.Combine(client.DataDirectory, "settings", "settings.json");
                var settings = JsonText.ParseFile(settingsPath) as JsonObject ?? new JsonObject();
                if (settings["enabledThemes"] is not JsonArray enabled)
                {
                    enabled = new JsonArray();
                    settings["enabledThemes"] = enabled;
                }
                if (!enabled.Any(e => e?.GetValue<string>() == fileName))
                {
                    enabled.Add(fileName);
                    journal.Record(settingsPath);
                    JsonText.Write(settingsPath, settings);
                }
            }
            else
            {
                // 채널(stable/ptb/canary)별 상태 파일. 없으면 stable 생성.
                var dataDir = Path.Combine(client.DataDirectory, "data");
                var channels = Directory.Exists(dataDir) ? Directory.GetDirectories(dataDir) : [];
                if (channels.Length == 0)
                    channels = [Path.Combine(dataDir, "stable")];
                foreach (var channel in channels)
                {
                    var statePath = Path.Combine(channel, "themes.json");
                    var state = JsonText.ParseFile(statePath) as JsonObject ?? new JsonObject();
                    state[themeName] = true;
                    journal.Record(statePath);
                    JsonText.Write(statePath, state);
                }
            }
            applied.Add(client.Name);
        }

        fontInstalled ??= FontRegistry.IsInstalled;
        foreach (var font in ReadFonts(sourceCss).Where(f => !fontInstalled(f)))
            warnings.Add($"Discord: font '{font}' is not installed, Discord will use a fallback font.");

        if (IsDiscordRunning())
            warnings.Add("Discord: fully quit Discord (also from the tray) and open it again to load the theme.");
        return applied;
    }
}
