using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MoniMS.Core.Desktop;

namespace MoniMS.App.ViewModels;

/// <summary>색 표의 한 칸.</summary>
public sealed record ColorSwatch(string Hex, Brush Brush);

/// <summary>
/// 색 고르기: 드롭다운처럼 생긴 버튼을 누르면 색 표가 펼쳐지고, "Windows 강조색 따라가기"를 고를 수 있다.
/// 정보 위젯 그래프 색, 음악 위젯 버튼 색에 쓴다 (설정 창의 ColorPickerViewModel 템플릿).
/// 값은 #RRGGBB, null이면 Windows 강조색.
/// </summary>
public sealed partial class ColorPickerViewModel : ObservableObject
{
    /// <summary>색 표 (가로 색상 × 세로 밝기 + 회색 줄). 모든 색 고르기가 같은 표를 쓴다.</summary>
    private static readonly IReadOnlyList<ColorSwatch> Palette = ColorUtil.BuildPickerPalette()
        .Select(c => new ColorSwatch(c.ToString(), Frozen(c)))
        .ToList();

    private readonly Func<Rgb> _windowsAccent;
    private readonly Action<string?> _changed;
    private string? _color;
    private bool _syncing;

    /// <param name="name">무엇의 색인지 (화면 읽기 프로그램용, 예: "graph color").</param>
    /// <param name="windowsAccent">지금 Windows 강조색.</param>
    /// <param name="changed">사용자가 색을 바꿨을 때 (null = Windows 강조색).</param>
    public ColorPickerViewModel(string name, Func<Rgb> windowsAccent, Action<string?> changed)
    {
        Name = name;
        _windowsAccent = windowsAccent;
        _changed = changed;
        Load(null);
    }

    public string Name { get; }

    public string ChooseText => $"Choose {Name}";

    public IReadOnlyList<ColorSwatch> ColorSwatches => Palette;

    public int ColorColumns => ColorUtil.PickerColumns;

    /// <summary>고른 색 (#RRGGBB). null이면 Windows 강조색.</summary>
    public string? Color => _color;

    /// <summary>표에서 고른 칸. 표에 없는 색(예전에 직접 입력한 색)이거나 Windows 강조색이면 null.</summary>
    [ObservableProperty] private ColorSwatch? _selectedSwatch;

    [ObservableProperty] private bool _useWindowsAccent;

    /// <summary>색 표 팝업이 열려 있는지. 칸을 고르면 닫힌다.</summary>
    [ObservableProperty] private bool _isOpen;

    /// <summary>지금 쓰이는 색 (미리보기 칸).</summary>
    public Brush PreviewBrush => Frozen(Current());

    /// <summary>버튼 글자: 표의 색이면 코드, 표에 없는 색이면 Custom, 강조색이면 Windows accent.</summary>
    public string Text =>
        UseWindowsAccent ? "Windows accent"
        : SelectedSwatch is null ? $"Custom · {Current()}"
        : Current().ToString();

    /// <summary>설정에서 읽은 값을 보여 준다 (알리지 않음).</summary>
    public void Load(string? hex)
    {
        _color = IsValidHex(hex) ? hex!.Trim() : null;
        Sync();
    }

    partial void OnSelectedSwatchChanged(ColorSwatch? value)
    {
        if (_syncing || value is null)
            return;
        Set(value.Hex);
        IsOpen = false;
    }

    partial void OnUseWindowsAccentChanged(bool value)
    {
        // 끄면 지금 보이는 강조색에서 시작한다 (표에 같은 색이 있으면 그 칸이 선택됨)
        if (!_syncing)
            Set(value ? null : _windowsAccent().ToString());
    }

    private void Set(string? hex)
    {
        _color = hex;
        Sync();
        _changed(hex);
    }

    /// <summary>값에 맞춰 체크박스·표 선택·미리보기를 맞춘다.</summary>
    private void Sync()
    {
        _syncing = true;
        UseWindowsAccent = _color is null;
        SelectedSwatch = _color is null ? null : Palette.FirstOrDefault(s => Rgb.Parse(s.Hex) == Rgb.Parse(_color));
        _syncing = false;
        OnPropertyChanged(nameof(PreviewBrush));
        OnPropertyChanged(nameof(Text));
    }

    private Rgb Current() => _color is null ? _windowsAccent() : Rgb.Parse(_color);

    public static bool IsValidHex(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return false;
        var t = s.Trim().TrimStart('#');
        return t.Length == 6 && t.All(Uri.IsHexDigit);
    }

    private static SolidColorBrush Frozen(Rgb c)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }
}
