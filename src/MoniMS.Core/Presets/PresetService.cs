using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MoniMS.Core.Desktop;

namespace MoniMS.Core.Presets;

public sealed record PresetApplyResult(PresetParts Applied, IReadOnlyList<string> Warnings);

/// <summary>현재 바탕화면 상태를 프리셋으로 캡처하고, 프리셋을 적용하는 진입점.</summary>
public sealed class PresetService
{
    private readonly IPresetStore _store;
    private readonly IWallpaperService _wallpaper;
    private readonly IThemeService _theme;
    private readonly IDesktopIconService _icons;
    private readonly IWidgetLayoutHost _widget;
    private readonly ILogger<PresetService> _logger;

    public PresetService(IPresetStore store, IWallpaperService wallpaper, IThemeService theme,
        IDesktopIconService icons, IWidgetLayoutHost widget, ILogger<PresetService>? logger = null)
    {
        _store = store;
        _wallpaper = wallpaper;
        _theme = theme;
        _icons = icons;
        _widget = widget;
        _logger = logger ?? NullLogger<PresetService>.Instance;
    }

    public event EventHandler<Preset>? PresetApplied;

    public IReadOnlyList<Preset> GetAll() => _store.GetAll();

    /// <summary>프리셋 폴더 (배경화면 복사본 위치).</summary>
    public string GetDirectory(Preset preset) => _store.GetPresetDirectory(preset.Id);

    public Preset Capture(string name, PresetParts parts = PresetParts.All)
    {
        var preset = new Preset { Name = name };
        CaptureInto(preset, parts);
        return preset;
    }

    /// <summary>기존 프리셋을 현재 상태로 덮어쓴다 (이름/ID 유지).</summary>
    public Preset Overwrite(Preset existing, PresetParts parts = PresetParts.All)
    {
        CaptureInto(existing, parts);
        return existing;
    }

    private void CaptureInto(Preset preset, PresetParts parts)
    {
        var dir = _store.GetPresetDirectory(preset.Id);

        if (parts.HasFlag(PresetParts.Wallpaper))
        {
            preset.Wallpaper = TryRun(() => _wallpaper.Capture(dir), "Wallpaper");
            RemoveUnreferencedImages(dir, preset.Wallpaper);
        }
        if (parts.HasFlag(PresetParts.Theme))
            preset.Theme = TryRun(_theme.Capture, "Theme");
        if (parts.HasFlag(PresetParts.Icons))
            preset.Icons = TryRun(_icons.Capture, "Icon layout");
        if (parts.HasFlag(PresetParts.Widget))
            preset.Widget = _widget.GetCurrentLayout().Clone();

        _store.Save(preset);
    }

    /// <summary>
    /// 덮어쓰기 시 예전 이미지 정리. 현재 배경화면이 이 폴더의 파일일 수도 있으므로
    /// 캡처(새 이름으로 복사)가 끝난 뒤에 지운다.
    /// </summary>
    private void RemoveUnreferencedImages(string dir, WallpaperSettings? settings)
    {
        var keep = new HashSet<string>(
            settings?.Monitors.Select(m => m.ImageFile).OfType<string>() ?? [],
            StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(dir))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || keep.Contains(name))
                continue;
            try
            {
                File.Delete(file);
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Image cleanup failed: {File}", file); // 사용 중이면 다음 기회에
            }
        }
    }

    public PresetApplyResult Apply(Preset preset, PresetParts parts = PresetParts.All)
    {
        var warnings = new List<string>();
        var applied = PresetParts.None;
        var dir = _store.GetPresetDirectory(preset.Id);
        parts &= preset.AvailableParts;

        // 순서: 테마 → 배경화면 (자동 강조색이 배경화면을 따라가므로) → 아이콘 → 위젯
        if (parts.HasFlag(PresetParts.Theme) && Try(() => _theme.Apply(preset.Theme!), "Theme", warnings))
            applied |= PresetParts.Theme;

        if (parts.HasFlag(PresetParts.Wallpaper) && Try(() => _wallpaper.Apply(preset.Wallpaper!, dir), "Wallpaper", warnings))
            applied |= PresetParts.Wallpaper;

        if (parts.HasFlag(PresetParts.Icons))
        {
            var ok = Try(() =>
            {
                var r = _icons.Apply(preset.Icons!);
                if (r.AutoArrangeEnabled)
                    warnings.Add("'Auto arrange icons' is on, so icon positions may not stick.");
                if (r.Missing > 0)
                    warnings.Add($"{r.Missing} icon(s) from the preset are no longer on the desktop (deleted or renamed). Presets restore positions only, so deleted files are not recreated.");
            }, "Icon layout", warnings);
            if (ok)
                applied |= PresetParts.Icons;
        }

        if (parts.HasFlag(PresetParts.Widget) && Try(() => _widget.ApplyLayout(preset.Widget!.Clone()), "Widget", warnings))
            applied |= PresetParts.Widget;

        PresetApplied?.Invoke(this, preset);
        return new PresetApplyResult(applied, warnings);
    }

    public void Rename(Preset preset, string newName)
    {
        preset.Name = newName;
        _store.Save(preset);
    }

    public void Delete(Preset preset) => _store.Delete(preset.Id);

    private T? TryRun<T>(Func<T> action, string what) where T : class
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{What} capture failed", what);
            return null;
        }
    }

    private bool Try(Action action, string what, List<string> warnings)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{What} apply failed", what);
            warnings.Add($"{what} failed: {ex.Message}");
            return false;
        }
    }
}
