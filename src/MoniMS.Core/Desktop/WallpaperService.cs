using System.Runtime.InteropServices;
using MoniMS.Core.Interop;
using MoniMS.Core.Presets;

namespace MoniMS.Core.Desktop;

/// <summary>IDesktopWallpaper COM으로 모니터별 배경화면을 읽고 쓴다. UI(STA) 스레드에서 호출할 것.</summary>
public sealed class WallpaperService : IWallpaperService
{
    private static readonly string ThemesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes");

    public WallpaperSettings Capture(string presetDirectory)
    {
        var dw = Create();
        try
        {
            var settings = new WallpaperSettings
            {
                Position = (WallpaperPosition)dw.GetPosition(),
                BackgroundColor = Rgb.FromAbgr(dw.GetBackgroundColor()).ToString(),
            };

            var count = dw.GetMonitorDevicePathCount();
            var stamp = DateTime.Now.Ticks.ToString("x", System.Globalization.CultureInfo.InvariantCulture);
            for (uint i = 0; i < count; i++)
            {
                var id = dw.GetMonitorDevicePathAt(i);
                RECT rect;
                try
                {
                    rect = dw.GetMonitorRECT(id);
                }
                catch (COMException)
                {
                    continue; // 연결 해제된 모니터
                }

                var source = ResolveSource(dw.GetWallpaper(id), (int)i);
                string? fileName = null;
                if (source is not null)
                {
                    var ext = Path.GetExtension(source);
                    if (string.IsNullOrEmpty(ext))
                        ext = ".jpg"; // TranscodedWallpaper는 확장자 없는 JPEG
                    fileName = $"wallpaper_{i}_{stamp}{ext}";
                    File.Copy(source, Path.Combine(presetDirectory, fileName), overwrite: true);
                }

                settings.Monitors.Add(new MonitorWallpaper
                {
                    MonitorId = id,
                    Index = (int)i,
                    Left = rect.Left,
                    Top = rect.Top,
                    Width = rect.Right - rect.Left,
                    Height = rect.Bottom - rect.Top,
                    ImageFile = fileName,
                });
            }
            return settings;
        }
        finally
        {
            Marshal.ReleaseComObject(dw);
        }
    }

    public void Apply(WallpaperSettings settings, string presetDirectory)
    {
        var dw = Create();
        try
        {
            dw.SetPosition((int)settings.Position);
            dw.SetBackgroundColor(Rgb.Parse(settings.BackgroundColor).ToColorRef());

            var withImage = settings.Monitors.Where(m => m.ImageFile is not null).ToList();
            if (withImage.Count == 0)
                return;

            var count = dw.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                var id = dw.GetMonitorDevicePathAt(i);
                try
                {
                    dw.GetMonitorRECT(id);
                }
                catch (COMException)
                {
                    continue;
                }

                // 같은 모니터 ID → 같은 순번 → 첫 번째 이미지 순으로 매칭
                var match = withImage.FirstOrDefault(m => m.MonitorId == id)
                            ?? withImage.FirstOrDefault(m => m.Index == i)
                            ?? withImage[0];
                var path = Path.Combine(presetDirectory, match.ImageFile!);
                if (File.Exists(path))
                    dw.SetWallpaper(id, path);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dw);
        }
    }

    public void SetForAllMonitors(string imagePath, WallpaperPosition position)
    {
        var dw = Create();
        try
        {
            dw.SetPosition((int)position);
            dw.SetWallpaper(null, imagePath); // monitorId가 null이면 모든 모니터
        }
        finally
        {
            Marshal.ReleaseComObject(dw);
        }
    }

    private static IDesktopWallpaper Create() => (IDesktopWallpaper)new DesktopWallpaperClass();

    /// <summary>원본이 지워졌으면 Windows가 보관하는 변환본(TranscodedWallpaper)을 사용.</summary>
    private static string? ResolveSource(string? path, int index)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            return path;

        string[] candidates =
        [
            Path.Combine(ThemesDir, $"Transcoded_{index:000}"),
            Path.Combine(ThemesDir, "TranscodedWallpaper"),
        ];
        return string.IsNullOrEmpty(path) ? null : candidates.FirstOrDefault(File.Exists);
    }
}
