using System.Text.Json.Serialization;

namespace MoniMS.Core.Shell;

/// <summary>
/// 가져온 작업표시줄/시작 메뉴 테마 (보관 위치: %APPDATA%\MoniMS\themes\{Id}\).
/// 원본 파일은 files\ 아래에 그대로 두고, 이 매니페스트(theme.json)는 분석 결과만 담는다.
/// </summary>
public sealed class ThemePackage
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string? Description { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>files\ 기준 상대 경로. 압축 파일 최상위 폴더 하나만 있으면 그 폴더.</summary>
    public string ContentRoot { get; set; } = "";

    public List<ThemeModConfig> Mods { get; set; } = [];

    /// <summary>files\ 기준 상대 경로.</summary>
    public List<string> Wallpapers { get; set; } = [];

    /// <summary>이 앱이 적용하지 않는 다른 프로그램용 설정 (Terminal, Discord ...).</summary>
    public List<string> OtherItems { get; set; } = [];

    [JsonIgnore]
    public string Directory { get; set; } = "";

    [JsonIgnore]
    public string FilesDirectory => Path.Combine(Directory, "files");

    public string FullPath(string relative) => Path.Combine(FilesDirectory, relative);
}

public sealed class ThemeModConfig
{
    public string ModId { get; set; } = "";

    /// <summary>files\ 기준 상대 경로의 Windhawk 설정 JSON (Windhawk "고급 → 모드 설정" 내보내기 형식).</summary>
    public string SourceFile { get; set; } = "";

    public int SettingCount { get; set; }

    /// <summary>시작 메뉴 스타일러의 webContentCustomJs처럼 임의 스크립트를 실행하는 설정이 있는지.</summary>
    public bool HasCustomScript { get; set; }
}
