using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MoniMS.Core.SystemInfo;

/// <summary>기본 게이트웨이가 있는 활성 어댑터를 "주 네트워크"로 보고 IP와 속도를 측정한다.</summary>
internal sealed class NetworkProvider : IDisposable
{
    /// <summary>
    /// 어댑터 전체 조회는 비싸서(수십 ms) 네트워크 변경 알림이 올 때만 다시 고른다.
    /// 알림을 놓치는 경우에 대비해 가끔은 그냥 다시 고른다.
    /// </summary>
    private static readonly TimeSpan ReselectInterval = TimeSpan.FromSeconds(60);

    private NetworkInterface? _primary;
    private DateTime _lastSelect = DateTime.MinValue;
    private volatile bool _changed = true;
    private long _lastRx;
    private long _lastTx;
    private readonly Stopwatch _sw = new();

    public NetworkProvider()
    {
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
    }

    public NetworkStatus? Read()
    {
        if (_changed || _primary is null || DateTime.UtcNow - _lastSelect > ReselectInterval)
            Select();

        if (_primary is null)
            return null;

        try
        {
            var stats = _primary.GetIPStatistics();
            var rx = stats.BytesReceived;
            var tx = stats.BytesSent;
            var seconds = _sw.Elapsed.TotalSeconds;
            _sw.Restart();

            double down = 0, up = 0;
            if (seconds > 0 && _lastRx > 0)
            {
                down = Math.Max(0, (rx - _lastRx) / seconds);
                up = Math.Max(0, (tx - _lastTx) / seconds);
            }
            _lastRx = rx;
            _lastTx = tx;

            // GetIPProperties()는 Select() 시점의 값이라 주소가 바뀌면 변경 알림 → 다시 고르기로 갱신된다
            var props = _primary.GetIPProperties();
            var ipv4 = props.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            var gw = props.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();

            return new NetworkStatus(_primary.Name, ipv4, gw, down, up);
        }
        catch (NetworkInformationException)
        {
            _primary = null;
            return null;
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => _changed = true;

    private void Select()
    {
        _changed = false;
        _lastSelect = DateTime.UtcNow;
        var candidate = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .OrderByDescending(n => n.GetIPProperties().GatewayAddresses
                .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any)))
            .ThenByDescending(n => n.Speed)
            .FirstOrDefault();

        if (candidate?.Id != _primary?.Id)
            _lastRx = _lastTx = 0; // 어댑터가 바뀌면 속도 계산 리셋
        _primary = candidate; // 같은 어댑터여도 새 IP 정보를 쓰도록 항상 교체
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
    }
}
