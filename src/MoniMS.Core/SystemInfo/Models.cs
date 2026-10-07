namespace MoniMS.Core.SystemInfo;

/// <summary>부팅 후 거의 바뀌지 않는 정보. 시작 시 한 번만 읽는다.</summary>
public sealed record StaticSystemInfo(
    string ComputerName,
    string UserName,
    string OsName,
    string OsDisplayVersion,
    string OsBuild,
    string CpuName,
    int LogicalProcessors,
    string Motherboard,
    IReadOnlyList<GpuAdapterInfo> Gpus,
    ulong TotalMemoryBytes);

public sealed record GpuAdapterInfo(string Name, ulong DedicatedMemoryBytes);

public sealed record DiskUsage(string Name, string Label, ulong TotalBytes, ulong FreeBytes)
{
    public ulong UsedBytes => TotalBytes - FreeBytes;
    public double UsedPercent => TotalBytes == 0 ? 0 : UsedBytes * 100.0 / TotalBytes;
}

public sealed record NetworkStatus(
    string AdapterName,
    string? LocalIPv4,
    string? Gateway,
    double DownloadBytesPerSec,
    double UploadBytesPerSec);

/// <summary>주기적으로 측정되는 실시간 값.</summary>
public sealed record SystemSnapshot(
    DateTimeOffset Timestamp,
    double CpuPercent,
    ulong MemoryUsedBytes,
    ulong MemoryTotalBytes,
    double? GpuPercent,
    ulong? GpuMemoryUsedBytes,
    IReadOnlyList<DiskUsage> Disks,
    NetworkStatus? Network,
    TimeSpan Uptime)
{
    public double MemoryPercent => MemoryTotalBytes == 0 ? 0 : MemoryUsedBytes * 100.0 / MemoryTotalBytes;
}
