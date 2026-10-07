using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MoniMS.Core.SystemInfo;

/// <summary>
/// "GPU Engine" 성능 카운터로 GPU 사용률을 계산한다.
/// 작업 관리자 방식: 엔진별(프로세스 합산) 사용률 중 최댓값.
/// 인스턴스(프로세스×엔진)가 수백 개라서 인스턴스마다 PerformanceCounter를 두면
/// 매번 카테고리 전체를 인스턴스 수만큼 다시 읽게 된다 (틱당 ~1초, 78MB 할당).
/// 그래서 틱마다 카테고리를 한 번만 읽고(ReadCategory) 이전 샘플과의 차이로 직접 계산한다.
/// </summary>
internal sealed partial class GpuUsageProvider
{
    private const string EngineCounter = "Utilization Percentage";
    private const string MemoryCounter = "Dedicated Usage";

    private readonly PerformanceCounterCategory? _engines;
    private readonly PerformanceCounterCategory? _memory;
    private Dictionary<string, CounterSample> _previous = new();

    public GpuUsageProvider()
    {
        _engines = TryOpen("GPU Engine");
        _memory = TryOpen("GPU Adapter Memory");
    }

    public (double? Percent, ulong? DedicatedUsed) Read()
    {
        if (_engines is null)
            return (null, null);

        InstanceDataCollection engines;
        try
        {
            engines = _engines.ReadCategory()[EngineCounter];
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _previous.Clear();
            return (null, null);
        }

        // 사라진 프로세스는 이번 샘플에 없으므로 이전 샘플 목록도 매번 새로 만든다
        var current = new Dictionary<string, CounterSample>(_previous.Count);
        var perEngine = new Dictionary<string, double>();
        if (engines is not null)
        {
            foreach (InstanceData data in engines.Values)
            {
                var sample = data.Sample;
                current[data.InstanceName] = sample;
                if (!_previous.TryGetValue(data.InstanceName, out var old))
                    continue; // 새 인스턴스: 다음 틱부터 계산
                var key = EngineKey(data.InstanceName);
                perEngine[key] = perEngine.GetValueOrDefault(key) + Math.Max(0, CounterSample.Calculate(old, sample));
            }
        }
        _previous = current;

        var percent = perEngine.Count == 0 ? 0 : Math.Clamp(perEngine.Values.Max(), 0, 100);
        return (percent, ReadDedicatedMemory());
    }

    /// <summary>어댑터별 전용 메모리 사용량 중 최댓값 (원시 값이라 이전 샘플이 필요 없다).</summary>
    private ulong? ReadDedicatedMemory()
    {
        if (_memory is null)
            return null;
        try
        {
            ulong? dedicated = null;
            if (_memory.ReadCategory()[MemoryCounter] is { } memory)
            {
                foreach (InstanceData data in memory.Values)
                    dedicated = Math.Max(dedicated ?? 0, (ulong)Math.Max(0, data.RawValue));
            }
            return dedicated;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>"pid_123_luid_0x0_0xABC_phys_0_eng_3_engtype_3D" → "luid_0x0_0xABC_phys_0_eng_3"</summary>
    internal static string EngineKey(string instanceName)
    {
        var m = EngineKeyRegex().Match(instanceName);
        return m.Success ? m.Value : instanceName;
    }

    [GeneratedRegex(@"luid_0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+_phys_\d+_eng_\d+")]
    private static partial Regex EngineKeyRegex();

    private static PerformanceCounterCategory? TryOpen(string category)
    {
        try
        {
            return PerformanceCounterCategory.Exists(category) ? new PerformanceCounterCategory(category) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
