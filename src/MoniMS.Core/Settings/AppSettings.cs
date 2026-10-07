using MoniMS.Core.Presets;

namespace MoniMS.Core.Settings;

/// <summary>앱 전역 설정 (%APPDATA%\MoniMS\settings.json).</summary>
public sealed class AppSettings
{
    public const int CurrentVersion = 2;

    /// <summary>설정 파일 형식 버전. 기본값 0 = 버전 정보가 없던 예전 파일.</summary>
    public int Version { get; set; }

    /// <summary>현재 위젯 레이아웃. 프리셋을 적용하면 여기로 복사된다.</summary>
    public WidgetLayout Widget { get; set; } = new();

    public string? LastAppliedPresetId { get; set; }

    /// <summary>설정 창 테마.</summary>
    public UiTheme UiTheme { get; set; } = UiTheme.Light;

    /// <summary>자동 감지가 안 될 때 사용자가 지정한 Windhawk 설치 폴더.</summary>
    public string? WindhawkPath { get; set; }

    /// <summary>측정 주기(ms).</summary>
    public int RefreshIntervalMs { get; set; } = 1000;
}

public enum UiTheme
{
    Light,
    Dark,
}
