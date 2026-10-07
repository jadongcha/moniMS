using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MoniMS.Core.Interop;

namespace MoniMS.Core.SystemInfo;

public sealed class SystemMonitor : ISystemMonitor
{
    private readonly ILogger<SystemMonitor> _logger;
    private readonly Lazy<StaticSystemInfo> _static = new(StaticInfoReader.Read);
    private CpuUsageProvider? _cpu;
    private GpuUsageProvider? _gpu;
    private readonly NetworkProvider _network = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _tick;

    public SystemMonitor(ILogger<SystemMonitor>? logger = null)
    {
        _logger = logger ?? NullLogger<SystemMonitor>.Instance;
    }

    public StaticSystemInfo StaticInfo => _static.Value;

    public SystemSnapshot? Latest { get; private set; }

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    public event EventHandler<SystemSnapshot>? SnapshotUpdated;

    public void Start()
    {
        if (_loop is not null)
            return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        _loop = null;
        _cts?.Dispose();
        _cts = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        // 성능 카운터 초기화는 수백 ms가 걸려서 백그라운드에서 수행
        _cpu ??= new CpuUsageProvider();
        _gpu ??= new GpuUsageProvider();
        _ = _static.Value;

        IReadOnlyList<DiskUsage> disks = [];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 디스크는 자주 바뀌지 않으므로 5틱마다 갱신
                if (_tick++ % 5 == 0)
                    disks = ReadDisks();

                var snapshot = Sample(disks);
                Latest = snapshot;
                SnapshotUpdated?.Invoke(this, snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to sample system info");
            }

            try
            {
                await Task.Delay(Interval, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private SystemSnapshot Sample(IReadOnlyList<DiskUsage> disks)
    {
        var mem = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        NativeMethods.GlobalMemoryStatusEx(ref mem);
        var (gpuPercent, gpuMem) = _gpu!.Read();

        return new SystemSnapshot(
            Timestamp: DateTimeOffset.Now,
            CpuPercent: _cpu!.Read(),
            MemoryUsedBytes: mem.ullTotalPhys - mem.ullAvailPhys,
            MemoryTotalBytes: mem.ullTotalPhys,
            GpuPercent: gpuPercent,
            GpuMemoryUsedBytes: gpuMem,
            Disks: disks,
            Network: _network.Read(),
            Uptime: TimeSpan.FromMilliseconds(Environment.TickCount64));
    }

    private static List<DiskUsage> ReadDisks()
    {
        var result = new List<DiskUsage>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType == DriveType.Fixed && d.IsReady)
                    result.Add(new DiskUsage(d.Name.TrimEnd('\\'), d.VolumeLabel, (ulong)d.TotalSize, (ulong)d.TotalFreeSpace));
            }
            catch (IOException)
            {
            }
        }
        return result;
    }

    public void Dispose()
    {
        Stop();
        _cpu?.Dispose();
        _gpu?.Dispose();
    }
}
