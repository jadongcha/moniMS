using System.Management;
using Microsoft.Win32;
using MoniMS.Core.Interop;

namespace MoniMS.Core.SystemInfo;

internal static class StaticInfoReader
{
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static StaticSystemInfo Read()
    {
        using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var build = cv?.GetValue("CurrentBuild") as string ?? Environment.OSVersion.Version.Build.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ubr = cv?.GetValue("UBR") is int u ? u : 0;
        var edition = cv?.GetValue("EditionID") as string ?? "";
        var display = cv?.GetValue("DisplayVersion") as string ?? "";

        return new StaticSystemInfo(
            ComputerName: Environment.MachineName,
            UserName: Environment.UserName,
            OsName: BuildOsName(int.TryParse(build, out var b) ? b : 0, edition),
            OsDisplayVersion: display,
            OsBuild: ubr > 0 ? $"{build}.{ubr}" : build,
            CpuName: ReadCpuName(),
            LogicalProcessors: Environment.ProcessorCount,
            Motherboard: ReadMotherboard(),
            Gpus: ReadGpus(),
            TotalMemoryBytes: ReadTotalMemory());
    }

    /// <summary>
    /// 레지스트리 ProductName은 Windows 11에서도 "Windows 10"으로 남아 있어서 빌드 번호로 판단한다.
    /// </summary>
    internal static string BuildOsName(int build, string editionId)
    {
        var name = build >= 22000 ? "Windows 11" : "Windows 10";
        var edition = editionId switch
        {
            "Professional" => "Pro",
            "Core" => "Home",
            "CoreSingleLanguage" => "Home Single Language",
            "Enterprise" => "Enterprise",
            "Education" => "Education",
            "ProfessionalWorkstation" => "Pro for Workstations",
            "" => "",
            _ => editionId,
        };
        return string.IsNullOrEmpty(edition) ? name : $"{name} {edition}";
    }

    private static string ReadCpuName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Unknown CPU";
    }

    private static string ReadMotherboard()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    return $"{obj["Manufacturer"]} {obj["Product"]}".Trim();
                }
            }
        }
        catch (ManagementException)
        {
        }
        return "Unknown";
    }

    /// <summary>
    /// WMI의 AdapterRAM은 uint32라서 4GB 이상을 표현 못한다. 드라이버 레지스트리의 qwMemorySize를 사용.
    /// </summary>
    private static List<GpuAdapterInfo> ReadGpus()
    {
        var list = new List<GpuAdapterInfo>();
        using var cls = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
        if (cls is null)
            return list;

        foreach (var sub in cls.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
        {
            try
            {
                using var key = cls.OpenSubKey(sub);
                var name = key?.GetValue("DriverDesc") as string;
                if (string.IsNullOrWhiteSpace(name) || name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Remote", StringComparison.OrdinalIgnoreCase))
                    continue;

                ulong mem = key?.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long l => (ulong)l,
                    byte[] bytes when bytes.Length >= 8 => BitConverter.ToUInt64(bytes, 0),
                    byte[] bytes when bytes.Length >= 4 => BitConverter.ToUInt32(bytes, 0),
                    int i => (uint)i,
                    _ => 0,
                };
                if (list.All(g => g.Name != name))
                    list.Add(new GpuAdapterInfo(name, mem));
            }
            catch (System.Security.SecurityException)
            {
                // 일부 하위 키는 권한이 없어서 건너뜀
            }
        }
        return list;
    }

    private static ulong ReadTotalMemory()
    {
        var status = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        return NativeMethods.GlobalMemoryStatusEx(ref status) ? status.ullTotalPhys : 0;
    }
}
