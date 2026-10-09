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
    private readonly IImageWidgetHost? _imageWidget;
    private readonly ILogger<PresetService> _logger;

    public PresetService(IPresetStore store, IWallpaperService wallpaper, IThemeService theme,
        IDesktopIconService icons, IWidgetLayoutHost widget, ILogger<PresetService>? logger = null,
        IImageWidgetHost? imageWidget = null)
    {
        _store = store;
        _wallpaper = wallpaper;
        _theme = theme;
        _icons = icons;
        _widget = widget;
        _imageWidget = imageWidget;
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

    /// <summary>
    /// 기존 프리셋을 현재 상태로 덮어쓴다 (이름/ID 유지). parts가 없으면 <see cref="Preset.OverwriteParts"/>
    /// (원래 들어 있던 항목 + 예전 프리셋이면 사진 위젯).
    /// </summary>
    public Preset Overwrite(Preset existing, PresetParts? parts = null)
    {
        CaptureInto(existing, parts ?? existing.OverwriteParts);
        return existing;
    }

    private void CaptureInto(Preset preset, PresetParts parts)
    {
        var dir = _store.GetPresetDirectory(preset.Id);

        if (parts.HasFlag(PresetParts.Wallpaper))
            preset.Wallpaper = TryRun(() => _wallpaper.Capture(dir), "Wallpaper");
        if (parts.HasFlag(PresetParts.Theme))
            preset.Theme = TryRun(_theme.Capture, "Theme");
        if (parts.HasFlag(PresetParts.Icons))
            preset.Icons = TryRun(_icons.Capture, "Icon layout");
        if (parts.HasFlag(PresetParts.Widget))
            preset.Widget = _widget.GetCurrentLayout().Clone();
        if (parts.HasFlag(PresetParts.ImageWidget) && _imageWidget is not null)
            preset.ImageWidget = TryRun(() => CaptureImageWidget(dir), "Image widget");

        if (parts.HasFlag(PresetParts.Wallpaper) || parts.HasFlag(PresetParts.ImageWidget))
            RemoveUnreferencedImages(dir, preset);
        preset.SchemaVersion = Preset.CurrentSchemaVersion; // 이제 지금 버전의 항목 선택을 따른다
        _store.Save(preset);
    }

    /// <summary>사진 위젯 상태 + 이미지 복사본 (프리셋을 지우기 전까지 이미지가 남도록 프리셋 폴더에).</summary>
    private ImageWidgetLayout CaptureImageWidget(string dir)
    {
        var layout = _imageWidget!.GetCurrentLayout().Clone();
        if (layout.ImagePath is { } source && File.Exists(source))
        {
            var stamp = DateTime.Now.Ticks.ToString("x", System.Globalization.CultureInfo.InvariantCulture);
            var fileName = $"image_{stamp}{Path.GetExtension(source).ToLowerInvariant()}";
            File.Copy(source, Path.Combine(dir, fileName), overwrite: true);
            layout.ImagePath = fileName;
        }
        else
        {
            layout.ImagePath = null;
        }
        return layout;
    }

    /// <summary>
    /// 덮어쓰기 시 예전 이미지(배경화면·사진 위젯) 정리. 현재 배경화면이 이 폴더의 파일일 수도 있으므로
    /// 캡처(새 이름으로 복사)가 끝난 뒤에 지운다.
    /// </summary>
    private void RemoveUnreferencedImages(string dir, Preset preset)
    {
        var keep = new HashSet<string>(
            (preset.Wallpaper?.Monitors.Select(m => m.ImageFile) ?? []).Append(preset.ImageWidget?.ImagePath).OfType<string>(),
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
        parts &= preset.AvailableParts;
        var applied = ApplyDesktop(preset, parts, warnings) | ApplyWidget(preset, parts, warnings);
        PresetApplied?.Invoke(this, preset);
        return new PresetApplyResult(applied, warnings);
    }

    /// <summary>
    /// <see cref="Apply"/>와 같지만 UI가 멈추지 않는다. 바탕화면 쪽(테마 변경 알림은 모든 창의 응답을 기다리고,
    /// 배경화면·아이콘은 탐색기를 기다린다)은 별도 STA 스레드에서, 위젯은 호출한 UI 스레드에서 적용한다.
    /// </summary>
    public async Task<PresetApplyResult> ApplyAsync(Preset preset, PresetParts parts = PresetParts.All)
    {
        var warnings = new List<string>();
        parts &= preset.AvailableParts;
        var applied = await RunOnStaThread(() => ApplyDesktop(preset, parts, warnings));
        applied |= ApplyWidget(preset, parts, warnings); // await 뒤라 다시 호출한 (UI) 스레드
        PresetApplied?.Invoke(this, preset);
        return new PresetApplyResult(applied, warnings);
    }

    private static Task<T> RunOnStaThread<T>(Func<T> action)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(action());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "MoniMS.PresetApply",
        };
        thread.SetApartmentState(ApartmentState.STA); // 배경화면 COM
        thread.Start();
        return tcs.Task;
    }

    /// <summary>테마 → 배경화면 (자동 강조색이 배경화면을 따라가므로) → 아이콘. UI 스레드가 아니어도 된다.</summary>
    private PresetParts ApplyDesktop(Preset preset, PresetParts parts, List<string> warnings)
    {
        var applied = PresetParts.None;
        var dir = _store.GetPresetDirectory(preset.Id);

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
        return applied;
    }

    /// <summary>위젯 창을 바꾸므로 UI 스레드에서.</summary>
    private PresetParts ApplyWidget(Preset preset, PresetParts parts, List<string> warnings)
    {
        var applied = PresetParts.None;
        if (parts.HasFlag(PresetParts.Widget) && Try(() => _widget.ApplyLayout(preset.Widget!.Clone()), "Widget", warnings))
            applied |= PresetParts.Widget;
        if (parts.HasFlag(PresetParts.ImageWidget) && _imageWidget is not null &&
            Try(() => _imageWidget.ApplyLayout(ResolveImageWidget(preset, warnings)), "Image widget", warnings))
            applied |= PresetParts.ImageWidget;
        return applied;
    }

    /// <summary>프리셋의 사진 위젯 → 이미지 파일 이름을 프리셋 폴더 안의 전체 경로로.</summary>
    private ImageWidgetLayout ResolveImageWidget(Preset preset, List<string> warnings)
    {
        var layout = preset.ImageWidget!.Clone();
        if (layout.ImagePath is { } file)
        {
            var full = Path.Combine(_store.GetPresetDirectory(preset.Id), Path.GetFileName(file));
            if (File.Exists(full))
            {
                layout.ImagePath = full;
            }
            else
            {
                layout.ImagePath = null;
                warnings.Add("Image widget: the saved image is missing from the preset, so the image widget was hidden.");
            }
        }
        return layout;
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
