using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MoniMS.Core.SystemInfo;

/// <summary>
/// "GPU Engine" 성능 카운터로 GPU 사용률을 계산한다.
/// 작업 관리자 방식: 엔진별(프로세스 합산) 사용률 중 최댓값.
/// 프로세스가 생기고 사라지므로 인스턴스 목록은 주기적으로 갱신한다.
/// </summary>
internal sealed partial class GpuUsageProvider : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, PerformanceCounter> _engineCounters = new();
    private readonly Dictionary<string, PerformanceCounter> _memoryCounters = new();
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly bool _available;

    public GpuUsageProvider()
    {
        try
        {
            _available = PerformanceCounterCategory.Exists("GPU Engine");
        }
        catch (Exception)
        {
            _available = false;
        }
    }

    public (double? Percent, ulong? DedicatedUsed) Read()
    {
        if (!_available)
            return (null, null);

        if (DateTime.UtcNow - _lastRefresh > RefreshInterval)
            RefreshInstances();

        var perEngine = new Dictionary<string, double>();
        foreach (var (instance, counter) in _engineCounters)
        {
            float value;
            try
            {
                value = counter.NextValue();
            }
            catch (InvalidOperationException)
            {
                continue; // 프로세스가 종료된 인스턴스
            }
            var key = EngineKey(instance);
            perEngine[key] = perEngine.GetValueOrDefault(key) + value;
        }

        ulong? dedicated = null;
        foreach (var counter in _memoryCounters.Values)
        {
            try
            {
                var v = (ulong)counter.NextValue();
                dedicated = Math.Max(dedicated ?? 0, v);
            }
            catch (InvalidOperationException)
            {
            }
        }

        var percent = perEngine.Count == 0 ? 0 : Math.Clamp(perEngine.Values.Max(), 0, 100);
        return (percent, dedicated);
    }

    /// <summary>"pid_123_luid_0x0_0xABC_phys_0_eng_3_engtype_3D" → "luid_0x0_0xABC_phys_0_eng_3"</summary>
    internal static string EngineKey(string instanceName)
    {
        var m = EngineKeyRegex().Match(instanceName);
        return m.Success ? m.Value : instanceName;
    }

    [GeneratedRegex(@"luid_0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+_phys_\d+_eng_\d+")]
    private static partial Regex EngineKeyRegex();

    private void RefreshInstances()
    {
        _lastRefresh = DateTime.UtcNow;
        Sync(_engineCounters, "GPU Engine", "Utilization Percentage", _ => true);
        Sync(_memoryCounters, "GPU Adapter Memory", "Dedicated Usage", _ => true);
    }

    private static void Sync(Dictionary<string, PerformanceCounter> map, string category, string counterName, Func<string, bool> filter)
    {
        string[] names;
        try
        {
            names = new PerformanceCounterCategory(category).GetInstanceNames();
        }
        catch (Exception)
        {
            return;
        }

        var current = new HashSet<string>(names.Where(filter));
        foreach (var stale in map.Keys.Where(k => !current.Contains(k)).ToList())
        {
            map[stale].Dispose();
            map.Remove(stale);
        }
        foreach (var name in current.Where(n => !map.ContainsKey(n)))
        {
            try
            {
                var c = new PerformanceCounter(category, counterName, name, readOnly: true);
                c.NextValue();
                map[name] = c;
            }
            catch (Exception)
            {
            }
        }
    }

    public void Dispose()
    {
        foreach (var c in _engineCounters.Values) c.Dispose();
        foreach (var c in _memoryCounters.Values) c.Dispose();
        _engineCounters.Clear();
        _memoryCounters.Clear();
    }
}
