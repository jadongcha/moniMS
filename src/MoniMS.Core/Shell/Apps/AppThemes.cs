using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MoniMS.Core.Shell.Apps;

/// <summary>Windhawk 말고 다른 앱에 적용하는 테마 종류.</summary>
public enum AppThemeKind
{
    WindowsTerminal,
    Discord,
    Komorebi,
    Yasb,
}

/// <summary>테마 패키지 안에서 찾은 앱별 테마 파일.</summary>
public sealed class ThemeAppConfig
{
    public AppThemeKind Kind { get; set; }

    /// <summary>files\ 기준 상대 경로 (기본으로 적용할 파일).</summary>
    public string SourceFile { get; set; } = "";

    /// <summary>UI에 보여줄 한 줄 설명 (예: 색 구성표 이름, 테마 이름).</summary>
    public string Summary { get; set; } = "";

    /// <summary>
    /// 같은 앱용 테마가 여러 개 들어 있으면 전부 (첫 번째 = <see cref="SourceFile"/>). 하나뿐이면 비어 있다.
    /// 예: Discord 테마 두 개, YASB 기본 + "Okinami Theme".
    /// </summary>
    public List<ThemeAppVariant> Variants { get; set; } = [];

    /// <summary>고를 수 있는 테마 목록 (하나뿐이어도 그 하나를 담아 반환).</summary>
    public IReadOnlyList<ThemeAppVariant> AllVariants() =>
        Variants.Count > 0 ? Variants : [new ThemeAppVariant { Name = Summary, SourceFile = SourceFile, Summary = Summary }];

    public static string DisplayName(AppThemeKind kind) => kind switch
    {
        AppThemeKind.WindowsTerminal => "Windows Terminal",
        AppThemeKind.Discord => "Discord",
        AppThemeKind.Komorebi => "Komorebi",
        AppThemeKind.Yasb => "YASB",
        _ => kind.ToString(),
    };

    /// <summary>복원한 파일 경로로 어느 앱 것인지 (표시용).</summary>
    public static string DisplayNameForPath(string path)
    {
        var p = path.Replace('/', '\\');
        if (p.Contains("Terminal", StringComparison.OrdinalIgnoreCase))
            return DisplayName(AppThemeKind.WindowsTerminal);
        if (p.Contains(@"\yasb\", StringComparison.OrdinalIgnoreCase))
            return DisplayName(AppThemeKind.Yasb);
        if (p.Contains("komorebi", StringComparison.OrdinalIgnoreCase) || p.EndsWith(@"\whkdrc", StringComparison.OrdinalIgnoreCase))
            return DisplayName(AppThemeKind.Komorebi);
        return DisplayName(AppThemeKind.Discord);
    }
}

/// <summary>한 앱용 테마 중 하나 (패키지에 여러 개 있을 때 고르는 단위).</summary>
public sealed class ThemeAppVariant
{
    public string Name { get; set; } = "";

    /// <summary>files\ 기준 상대 경로.</summary>
    public string SourceFile { get; set; } = "";

    public string Summary { get; set; } = "";

    /// <summary>목록(ComboBox)에 보이는 이름.</summary>
    public override string ToString() => Name;
}

/// <summary>
/// 테마 적용 중 바뀌는 파일의 "원래 내용"을 기록 → 복원할 때 그대로 되돌린다.
/// 원래 없던 파일은 null로 기록되어 복원 시 삭제된다.
/// </summary>
public sealed class FileJournal
{
    private readonly Dictionary<string, string?> _originals = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string?> Originals => _originals;

    /// <summary>파일을 바꾸기 직전에 호출. 같은 파일은 처음 한 번만 기록.</summary>
    public void Record(string path)
    {
        var full = Path.GetFullPath(path);
        if (_originals.ContainsKey(full))
            return;
        _originals[full] = File.Exists(full) ? File.ReadAllText(full) : null;
    }
}

internal static class JsonText
{
    public static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip, // Windows Terminal settings.json 에는 주석이 있다
        AllowTrailingCommas = true,
    };

    public static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 한글/기호를 \uXXXX로 바꾸지 않음
    };

    public static JsonNode? Parse(string text) => JsonNode.Parse(text.TrimStart('﻿'), documentOptions: Lenient);

    public static JsonNode? ParseFile(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : null;

    public static void Write(string path, JsonNode node)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, node.ToJsonString(Pretty), new System.Text.UTF8Encoding(false));
    }
}
