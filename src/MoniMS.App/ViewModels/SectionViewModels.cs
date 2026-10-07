using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MoniMS.Core.Presets;
using MoniMS.Core.SystemInfo;

namespace MoniMS.App.ViewModels;

/// <summary>위젯의 한 구역. 구역마다 DataTemplate(WidgetWindow.xaml)이 따로 있다.</summary>
public abstract class SectionViewModel : ObservableObject
{
    protected SectionViewModel(WidgetSection kind) => Kind = kind;

    public WidgetSection Kind { get; }

    public abstract void Update(SystemSnapshot snapshot, StaticSystemInfo info);

    public static SectionViewModel Create(WidgetSection kind, StaticSystemInfo info) => kind switch
    {
        WidgetSection.System => new SystemSectionViewModel(info),
        WidgetSection.Cpu => new GaugeSectionViewModel(kind, "CPU", (s, i) =>
            (s.CpuPercent, $"{i.LogicalProcessors} threads")),
        WidgetSection.Memory => new GaugeSectionViewModel(kind, "RAM", (s, _) =>
            (s.MemoryPercent, $"{Format.Bytes(s.MemoryUsedBytes)} / {Format.Bytes(s.MemoryTotalBytes)}")),
        WidgetSection.Gpu => new GaugeSectionViewModel(kind, "GPU", (s, i) =>
            (s.GpuPercent, GpuDetail(s, i))),
        WidgetSection.Disk => new DiskSectionViewModel(),
        WidgetSection.Network => new NetworkSectionViewModel(),
        WidgetSection.Uptime => new UptimeSectionViewModel(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string GpuDetail(SystemSnapshot s, StaticSystemInfo info)
    {
        var total = info.Gpus.Select(g => g.DedicatedMemoryBytes).DefaultIfEmpty(0UL).Max();
        if (s.GpuMemoryUsedBytes is { } used && total > 0)
            return $"VRAM {Format.Bytes(used)} / {Format.Bytes(total)}";
        if (s.GpuMemoryUsedBytes is { } u)
            return $"VRAM {Format.Bytes(u)}";
        return s.GpuPercent is null ? "unavailable" : "";
    }
}

public sealed class SystemSectionViewModel : SectionViewModel
{
    public SystemSectionViewModel(StaticSystemInfo info) : base(WidgetSection.System)
    {
        ComputerName = info.ComputerName;
        Os = string.IsNullOrEmpty(info.OsDisplayVersion) ? info.OsName : $"{info.OsName} {info.OsDisplayVersion}";
        OsBuild = info.OsBuild;
        Cpu = info.CpuName;
        Gpu = info.Gpus.Count == 0 ? "-" : string.Join("\n", info.Gpus.Select(g => g.Name));
        Ram = Format.Bytes(info.TotalMemoryBytes, 0);
        Board = info.Motherboard;
    }

    public string ComputerName { get; }
    public string Os { get; }
    public string OsBuild { get; }
    public string Cpu { get; }
    public string Gpu { get; }
    public string Ram { get; }
    public string Board { get; }

    public override void Update(SystemSnapshot snapshot, StaticSystemInfo info)
    {
    }
}

/// <summary>퍼센트 + 최근 60초 그래프.</summary>
public sealed partial class GaugeSectionViewModel : SectionViewModel
{
    public const int HistoryLength = 60;
    private readonly Func<SystemSnapshot, StaticSystemInfo, (double? Percent, string Detail)> _read;
    private readonly Queue<double> _history = new();

    public GaugeSectionViewModel(WidgetSection kind, string title,
        Func<SystemSnapshot, StaticSystemInfo, (double? Percent, string Detail)> read) : base(kind)
    {
        Title = title;
        _read = read;
    }

    public string Title { get; }

    [ObservableProperty] private string _percentText = "--";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private double _fraction;
    [ObservableProperty] private PointCollection _linePoints = [];
    [ObservableProperty] private PointCollection _areaPoints = [];

    public override void Update(SystemSnapshot snapshot, StaticSystemInfo info)
    {
        var (percent, detail) = _read(snapshot, info);
        Detail = detail;
        if (percent is not { } p)
        {
            PercentText = "--";
            return;
        }

        PercentText = p.ToString("0", CultureInfo.InvariantCulture) + "%";
        Fraction = p / 100.0;

        _history.Enqueue(p);
        while (_history.Count > HistoryLength)
            _history.Dequeue();
        BuildGraph();
    }

    /// <summary>캔버스 좌표계: 가로 0~59, 세로 0~100 (Viewbox가 실제 크기로 늘린다).</summary>
    private void BuildGraph()
    {
        var values = _history.ToArray();
        var offset = HistoryLength - values.Length; // 오른쪽 정렬
        var line = new PointCollection(values.Length);
        for (var i = 0; i < values.Length; i++)
            line.Add(new Point(offset + i, 100 - values[i]));

        var area = new PointCollection(line) { new Point(HistoryLength - 1, 100), new Point(offset, 100) };
        line.Freeze();
        area.Freeze();
        LinePoints = line;
        AreaPoints = area;
    }
}

public sealed partial class DiskItemViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private double _fraction;
}

public sealed class DiskSectionViewModel : SectionViewModel
{
    public DiskSectionViewModel() : base(WidgetSection.Disk)
    {
    }

    public ObservableCollection<DiskItemViewModel> Disks { get; } = [];

    public override void Update(SystemSnapshot snapshot, StaticSystemInfo info)
    {
        var disks = snapshot.Disks;
        while (Disks.Count > disks.Count)
            Disks.RemoveAt(Disks.Count - 1);
        while (Disks.Count < disks.Count)
            Disks.Add(new DiskItemViewModel());

        for (var i = 0; i < disks.Count; i++)
        {
            var d = disks[i];
            Disks[i].Name = string.IsNullOrEmpty(d.Label) ? d.Name : $"{d.Name} {d.Label}";
            Disks[i].Text = $"{Format.Bytes(d.UsedBytes)} / {Format.Bytes(d.TotalBytes)}";
            Disks[i].Fraction = d.UsedPercent / 100.0;
        }
    }
}

public sealed partial class NetworkSectionViewModel : SectionViewModel
{
    public NetworkSectionViewModel() : base(WidgetSection.Network)
    {
    }

    [ObservableProperty] private string _ip = "offline";
    [ObservableProperty] private string _adapter = "";
    [ObservableProperty] private string _download = "-";
    [ObservableProperty] private string _upload = "-";

    public override void Update(SystemSnapshot snapshot, StaticSystemInfo info)
    {
        var n = snapshot.Network;
        if (n is null)
        {
            Ip = "offline";
            Adapter = "";
            Download = Upload = "-";
            return;
        }
        Ip = n.LocalIPv4 ?? "no IPv4";
        Adapter = n.Gateway is null ? n.AdapterName : $"{n.AdapterName} · GW {n.Gateway}";
        Download = Format.Speed(n.DownloadBytesPerSec);
        Upload = Format.Speed(n.UploadBytesPerSec);
    }
}

public sealed partial class UptimeSectionViewModel : SectionViewModel
{
    public UptimeSectionViewModel() : base(WidgetSection.Uptime)
    {
    }

    [ObservableProperty] private string _uptime = "";
    [ObservableProperty] private string _clock = "";

    public override void Update(SystemSnapshot snapshot, StaticSystemInfo info)
    {
        Uptime = Format.Uptime(snapshot.Uptime);
        Clock = snapshot.Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
