using System.Diagnostics;

namespace MoniMS.Core.SystemInfo;

/// <summary>
/// 작업 관리자와 같은 값을 얻기 위해 "% Processor Utility"를 우선 사용하고, 없으면 "% Processor Time"으로 대체.
/// </summary>
internal sealed class CpuUsageProvider : IDisposable
{
    private readonly PerformanceCounter? _counter;

    public CpuUsageProvider()
    {
        _counter = TryCreate("Processor Information", "% Processor Utility", "_Total")
                   ?? TryCreate("Processor", "% Processor Time", "_Total");
        _counter?.NextValue(); // 첫 값은 항상 0이라 미리 한번 읽어둔다
    }

    public double Read() => _counter is null ? 0 : Math.Clamp(_counter.NextValue(), 0, 100);

    private static PerformanceCounter? TryCreate(string category, string counter, string instance)
    {
        try
        {
            return PerformanceCounterCategory.CounterExists(counter, category)
                ? new PerformanceCounter(category, counter, instance, readOnly: true)
                : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose() => _counter?.Dispose();
}
