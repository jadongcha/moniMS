using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MoniMS.Core.Shell.Apps;

/// <summary>Windhawk 말고 다른 앱에 적용하는 테마 종류.</summary>
public enum AppThemeKind
{
    WindowsTerminal,
    Discord,
}

/// <summary>테마 패키지 안에서 찾은 앱별 테마 파일.</summary>
public sealed class ThemeAppConfig
{
    public AppThemeKind Kind { get; set; }

    /// <summary>files\ 기준 상대 경로.</summary>
    public string SourceFile { get; set; } = "";

    /// <summary>UI에 보여줄 한 줄 설명 (예: 색 구성표 이름, 테마 이름).</summary>
    public string Summary { get; set; } = "";

    public static string DisplayName(AppThemeKind kind) => kind switch
    {
        AppThemeKind.WindowsTerminal => "Windows Terminal",
        AppThemeKind.Discord => "Discord",
        _ => kind.ToString(),
    };
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
