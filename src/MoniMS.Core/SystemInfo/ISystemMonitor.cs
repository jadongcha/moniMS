namespace MoniMS.Core.SystemInfo;

public interface ISystemMonitor : IDisposable
{
    StaticSystemInfo StaticInfo { get; }

    SystemSnapshot? Latest { get; }

    /// <summary>측정 주기마다 백그라운드 스레드에서 발생. UI에서는 Dispatcher로 넘겨서 사용.</summary>
    event EventHandler<SystemSnapshot>? SnapshotUpdated;

    TimeSpan Interval { get; set; }

    void Start();

    void Stop();
}
