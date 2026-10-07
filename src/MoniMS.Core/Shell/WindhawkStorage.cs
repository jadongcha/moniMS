using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MoniMS.Core.Shell;

public enum ModState
{
    NotInstalled,
    Enabled,
    Disabled,
}

/// <summary>설치된 Windhawk의 저장 방식. windhawk.ini의 [Storage] 섹션에서 읽는다.</summary>
public sealed record WindhawkInfo(
    bool Installed,
    string? AppRoot,
    bool Portable,
    string? RegistryKey,
    string? AppDataPath,
    string? Version)
{
    public static WindhawkInfo NotFound { get; } = new(false, null, false, null, null, null);
}

public interface IWindhawkStorage
{
    /// <summary>사용자가 직접 지정한 설치 폴더 (자동 감지 실패 시).</summary>
    string? CustomAppRoot { get; set; }

    WindhawkInfo Detect();

    ModState GetModState(string modId);

    IReadOnlyDictionary<string, object> ReadSettings(string modId);

    /// <summary>
    /// 모드 설정을 통째로 교체하고 SettingsChangeTime을 갱신한다 (Windhawk 엔진이 감지해 즉시 반영).
    /// 권한이 없으면 UnauthorizedAccessException.
    /// </summary>
    void WriteSettings(string modId, IReadOnlyDictionary<string, object> settings);
}

/// <summary>
/// Windhawk 저장소 접근. 구조는 Windhawk 소스(windhawk-core storage.rs / mods.rs)와 동일하게 맞춤:
/// <list type="bullet">
/// <item>설치형: {RegistryKey}\Engine\Mods\{modId} (Disabled, SettingsChangeTime) + \Settings (값: REG_SZ / REG_DWORD)</item>
/// <item>포터블: {AppDataPath}\Engine\Mods\{modId}.ini 의 [Mod], [Settings]</item>
/// </list>
/// 설정을 쓴 뒤 SettingsChangeTime(유닉스 초 &amp; 0x7fffffff)을 바꾸면 엔진이 모드에 변경을 알린다.
/// </summary>
public sealed partial class WindhawkStorage : IWindhawkStorage
{
    private const string DefaultRegistryKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\Windhawk";

    public string? CustomAppRoot { get; set; }

    public WindhawkInfo Detect()
    {
        foreach (var root in CandidateRoots())
        {
            var ini = Path.Combine(root, "windhawk.ini");
            if (!File.Exists(ini))
                continue;
            try
            {
                var storage = ParseStorageSection(File.ReadAllBytes(ini));
                var portable = storage.TryGetValue("Portable", out var p) && LeadingInt(p) != 0;
                var appData = storage.TryGetValue("AppDataPath", out var a) && a.Length > 0 ? ResolvePath(root, a) : null;
                var regKey = storage.TryGetValue("RegistryKey", out var r) && r.Length > 0 ? r : DefaultRegistryKey;
                string? version = null;
                var exe = Path.Combine(root, "windhawk.exe");
                if (File.Exists(exe))
                    version = FileVersionInfo.GetVersionInfo(exe).ProductVersion;
                return new WindhawkInfo(true, root, portable, portable ? null : regKey, appData, version);
            }
            catch (IOException)
            {
            }
        }
        return WindhawkInfo.NotFound;
    }

    public ModState GetModState(string modId)
    {
        var info = Detect();
        if (!info.Installed)
            return ModState.NotInstalled;

        if (info.Portable)
        {
            var ini = ModIni(info, modId);
            if (ini is null || !File.Exists(ini))
                return ModState.NotInstalled;
            return GetPrivateProfileIntW("Mod", "Disabled", 0, ini) != 0 ? ModState.Disabled : ModState.Enabled;
        }

        var (hive, sub) = ParseRegistryKey(info.RegistryKey!);
        using var key = hive.OpenSubKey($@"{sub}\Engine\Mods\{modId}");
        if (key is null)
            return ModState.NotInstalled;
        return key.GetValue("Disabled") is int d && d != 0 ? ModState.Disabled : ModState.Enabled;
    }

    public IReadOnlyDictionary<string, object> ReadSettings(string modId)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        var info = Detect();
        if (!info.Installed)
            return result;

        if (info.Portable)
        {
            var ini = ModIni(info, modId);
            if (ini is null || !File.Exists(ini))
                return result;
            foreach (var (name, value) in ReadIniSection(ini, "Settings"))
                result[name] = value; // ini에는 타입이 없음: 문자열로 보관 (복원 시 같은 텍스트로 기록됨)
            return result;
        }

        var (hive, sub) = ParseRegistryKey(info.RegistryKey!);
        using var key = hive.OpenSubKey($@"{sub}\Engine\Mods\{modId}\Settings");
        if (key is null)
            return result;
        foreach (var name in key.GetValueNames())
        {
            switch (key.GetValue(name))
            {
                case int i:
                    result[name] = i;
                    break;
                case string s:
                    result[name] = s;
                    break;
            }
        }
        return result;
    }

    public void WriteSettings(string modId, IReadOnlyDictionary<string, object> settings)
    {
        var info = Detect();
        if (!info.Installed)
            throw new InvalidOperationException("Windhawk is not installed.");

        var changeTime = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() & 0x7fffffff);

        if (info.Portable)
        {
            var ini = ModIni(info, modId) ?? throw new InvalidOperationException("Windhawk AppDataPath is missing.");
            // 섹션 삭제 후 다시 기록 (Windhawk의 전체 교체 방식과 동일)
            if (!WritePrivateProfileStringW("Settings", null, null, ini))
                ThrowWin32("clear settings");
            foreach (var (name, value) in settings)
            {
                if (!WritePrivateProfileStringW("Settings", name, ToText(value), ini))
                    ThrowWin32($"write {name}");
            }
            if (!WritePrivateProfileStringW("Mod", "SettingsChangeTime", changeTime.ToString(System.Globalization.CultureInfo.InvariantCulture), ini))
                ThrowWin32("write SettingsChangeTime");
            return;
        }

        var (hive, sub) = ParseRegistryKey(info.RegistryKey!);
        var modPath = $@"{sub}\Engine\Mods\{modId}";
        try
        {
            using var modKey = hive.OpenSubKey(modPath, writable: true)
                               ?? throw new InvalidOperationException($"Mod '{modId}' is not installed in Windhawk.");
            modKey.DeleteSubKeyTree("Settings", throwOnMissingSubKey: false);
            using (var settingsKey = modKey.CreateSubKey("Settings", writable: true))
            {
                foreach (var (name, value) in settings)
                {
                    if (value is int i)
                        settingsKey.SetValue(name, i, RegistryValueKind.DWord);
                    else
                        settingsKey.SetValue(name, ToText(value), RegistryValueKind.String);
                }
            }
            modKey.SetValue("SettingsChangeTime", changeTime, RegistryValueKind.DWord);
        }
        catch (System.Security.SecurityException ex)
        {
            throw new UnauthorizedAccessException(ex.Message, ex);
        }
    }

    // ---------- 감지 ----------

    private IEnumerable<string> CandidateRoots()
    {
        if (!string.IsNullOrWhiteSpace(CustomAppRoot))
            yield return CustomAppRoot!;

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            string? location = null;
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var un = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Windhawk");
                location = un?.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(location) && un?.GetValue("UninstallString") is string uninst)
                    location = Path.GetDirectoryName(uninst.Trim('"'));
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException or ArgumentException)
            {
            }
            if (!string.IsNullOrWhiteSpace(location))
                yield return location.Trim('"');
        }

        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windhawk");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Windhawk");
    }

    /// <summary>windhawk.ini 디코딩 (UTF-16LE BOM 또는 UTF-8) 후 [Storage] 섹션 키/값.</summary>
    internal static Dictionary<string, string> ParseStorageSection(byte[] bytes)
    {
        string text = bytes is [0xFF, 0xFE, ..]
            ? Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2)
            : Encoding.UTF8.GetString(bytes).TrimStart('﻿');

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inStorage = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inStorage = line[1..^1].Equals("Storage", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            var eq = line.IndexOf('=');
            if (inStorage && eq > 0)
                result[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return result;
    }

    internal static int LeadingInt(string s)
    {
        var i = 0;
        if (i < s.Length && s[i] is '+' or '-')
            i++;
        while (i < s.Length && char.IsAsciiDigit(s[i]))
            i++;
        return int.TryParse(s.AsSpan(0, i), out var v) ? v : 0;
    }

    private static string ResolvePath(string root, string raw)
    {
        var expanded = Environment.ExpandEnvironmentVariables(raw);
        return Path.GetFullPath(Path.Combine(root, expanded));
    }

    /// <summary>"HKEY_LOCAL_MACHINE\SOFTWARE\Windhawk" 또는 "HKLM\..." 형식.</summary>
    internal static (RegistryKey Hive, string SubKey) ParseRegistryKey(string value)
    {
        var idx = value.IndexOf('\\');
        var hiveName = idx < 0 ? value : value[..idx];
        var sub = idx < 0 ? "" : value[(idx + 1)..];
        RegistryKey hive = hiveName.ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" or "HKLM" => Registry.LocalMachine,
            "HKEY_CURRENT_USER" or "HKCU" => Registry.CurrentUser,
            "HKEY_USERS" or "HKU" => Registry.Users,
            _ => throw new NotSupportedException($"Unsupported registry hive: {hiveName}"),
        };
        return (hive, sub);
    }

    private static string? ModIni(WindhawkInfo info, string modId) =>
        info.AppDataPath is null ? null : Path.Combine(info.AppDataPath, "Engine", "Mods", modId + ".ini");

    private static string ToText(object value) => value switch
    {
        int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static IEnumerable<(string Name, string Value)> ReadIniSection(string file, string section)
    {
        var buffer = new char[1 << 20];
        var len = GetPrivateProfileSectionW(section, buffer, buffer.Length, file);
        foreach (var entry in new string(buffer, 0, len).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = entry.IndexOf('=');
            if (eq > 0)
                yield return (entry[..eq], entry[(eq + 1)..]);
        }
    }

    private static void ThrowWin32(string what)
    {
        var err = Marshal.GetLastPInvokeError();
        if (err == 5) // ERROR_ACCESS_DENIED
            throw new UnauthorizedAccessException($"Access denied ({what}).");
        throw new IOException($"Windhawk ini {what} failed (error {err}).");
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WritePrivateProfileStringW(string section, string? key, string? value, string file);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetPrivateProfileIntW(string section, string key, int defaultValue, string file);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetPrivateProfileSectionW(string section, [Out] char[] buffer, int size, string file);
}
