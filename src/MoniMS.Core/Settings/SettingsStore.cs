using System.Text.Json;
using MoniMS.Core.Presets;

namespace MoniMS.Core.Settings;

public interface ISettingsStore
{
    AppSettings Current { get; }

    void Save();
}

public sealed class SettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly object _lock = new();

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.DataDirectory, "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        Current = Load();
        if (Migrate(Current))
            Save();
    }

    public AppSettings Current { get; }

    public void Save()
    {
        lock (_lock)
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonDefaults.Options));
            File.Move(tmp, _path, overwrite: true);
        }
    }

    /// <summary>예전 설정 파일을 현재 형식으로 올린다. 바뀐 게 있으면 true.</summary>
    internal static bool Migrate(AppSettings settings)
    {
        if (settings.Version >= AppSettings.CurrentVersion)
            return false;
        if (settings.Version < 2)
        {
            // v2: 위젯 기본 크기 지정
            settings.Widget.Width = WidgetLayout.DefaultWidth;
            settings.Widget.Height = WidgetLayout.DefaultHeight;
        }
        if (settings.Version < 5)
        {
            // v3: 기본 너비 550 → 478 (화면 가로 비율 38.2% → 33.2%)
            // v5: 기본 높이 650 → 695 (화면 세로 비율 72.2% → 77.2%). v4에서 잠깐 쓴 830도 되돌림.
            settings.Widget.UpgradeLegacySize();
        }
        settings.Version = AppSettings.CurrentVersion;
        return true;
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonDefaults.Options) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // 손상된 설정은 백업해 두고 기본값으로 시작
            try
            {
                File.Copy(_path, _path + ".broken", overwrite: true);
            }
            catch (IOException)
            {
            }
        }
        return new AppSettings();
    }
}
