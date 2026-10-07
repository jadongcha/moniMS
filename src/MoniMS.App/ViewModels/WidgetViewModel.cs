using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MoniMS.Core.Presets;
using MoniMS.Core.SystemInfo;

namespace MoniMS.App.ViewModels;

public sealed partial class WidgetViewModel : ObservableObject
{
    private readonly ISystemMonitor _monitor;
    private readonly Dispatcher _dispatcher;

    public WidgetViewModel(ISystemMonitor monitor)
    {
        _monitor = monitor;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _monitor.SnapshotUpdated += OnSnapshot;
    }

    public ObservableCollection<SectionViewModel> Sections { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowWidth))]
    private double _width = 550;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowHeight))]
    private double _height = 650;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowWidth), nameof(WindowHeight))]
    private double _scale = 1.0;

    [ObservableProperty] private double _opacity = 0.92;
    [ObservableProperty] private bool _isEditMode;

    public double WindowWidth => Width * Scale;

    public double WindowHeight => Height * Scale;

    public void ApplyLayout(WidgetLayout layout)
    {
        Width = Math.Clamp(layout.Width, 200, 1200);
        Height = Math.Clamp(layout.Height, 150, 1600);
        Scale = Math.Clamp(layout.Scale, 0.5, 2.5);
        Opacity = Math.Clamp(layout.Opacity, 0.0, 1.0);

        // 순서나 구성이 바뀐 경우에만 다시 만든다 (그래프 기록 유지)
        if (!Sections.Select(s => s.Kind).SequenceEqual(layout.Sections))
        {
            var existing = Sections.ToDictionary(s => s.Kind);
            Sections.Clear();
            foreach (var kind in layout.Sections.Distinct())
                Sections.Add(existing.TryGetValue(kind, out var vm) ? vm : SectionViewModel.Create(kind, _monitor.StaticInfo));
            if (_monitor.Latest is { } latest)
                Refresh(latest);
        }
    }

    private void OnSnapshot(object? sender, SystemSnapshot snapshot) =>
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () => Refresh(snapshot));

    private void Refresh(SystemSnapshot snapshot)
    {
        var info = _monitor.StaticInfo;
        foreach (var section in Sections)
            section.Update(snapshot, info);
    }
}
