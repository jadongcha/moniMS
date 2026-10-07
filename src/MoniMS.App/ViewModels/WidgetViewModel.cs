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

    private (double Width, double Height, double Dpi) _screen;

    /// <summary>레이아웃 크기 (기준 화면 2880×1800에서의 크기, DIP).</summary>
    [ObservableProperty] private double _width = WidgetLayout.DefaultWidth;

    [ObservableProperty] private double _height = WidgetLayout.DefaultHeight;

    /// <summary>사용자 배율 (레이아웃 값).</summary>
    [ObservableProperty] private double _scale = 1.0;

    /// <summary>실제 창 크기 (DIP). 모니터 해상도에 맞춰 WidgetController가 계산한다.</summary>
    [ObservableProperty] private double _windowWidth = WidgetLayout.DefaultWidth;

    [ObservableProperty] private double _windowHeight = WidgetLayout.DefaultHeight;

    /// <summary>내용 배율 (화면 비율 맞춤 × 사용자 배율).</summary>
    [ObservableProperty] private double _contentScale = 1.0;

    [ObservableProperty] private double _opacity = 0.92;
    [ObservableProperty] private bool _isEditMode;

    /// <summary>모니터 해상도·배율에 맞춰 창 크기와 내용 배율을 정한다.</summary>
    public void FitToScreen(double screenWidth, double screenHeight, double dpiScale)
    {
        _screen = (screenWidth, screenHeight, dpiScale);
        Recalculate();
    }

    private void Recalculate()
    {
        var (w, h, s) = WidgetSizing.Fit(Width, Height, Scale, _screen.Width, _screen.Height, _screen.Dpi);
        WindowWidth = w;
        WindowHeight = h;
        ContentScale = s;
    }

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
        Recalculate();
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
