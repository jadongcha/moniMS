using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MoniMS.Core.SystemInfo;

/// <summary>기본 게이트웨이가 있는 활성 어댑터를 "주 네트워크"로 보고 IP와 속도를 측정한다.</summary>
internal sealed class NetworkProvider
{
    private static readonly TimeSpan ReselectInterval = TimeSpan.FromSeconds(10);

    private NetworkInterface? _primary;
    private DateTime _lastSelect = DateTime.MinValue;
    private long _lastRx;
    private long _lastTx;
    private readonly Stopwatch _sw = new();

    public NetworkStatus? Read()
    {
        if (_primary is null || DateTime.UtcNow - _lastSelect > ReselectInterval)
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

    private void Select()
    {
        _lastSelect = DateTime.UtcNow;
        var candidate = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .OrderByDescending(n => n.GetIPProperties().GatewayAddresses
                .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any)))
            .ThenByDescending(n => n.Speed)
            .FirstOrDefault();

        if (candidate?.Id != _primary?.Id)
        {
            _primary = candidate;
            _lastRx = _lastTx = 0; // 어댑터가 바뀌면 속도 계산 리셋
        }
    }
}
