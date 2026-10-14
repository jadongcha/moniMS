using MoniMS.Core.Presets;

namespace MoniMS.Core.Tests;

public class WidgetSnapTests
{
    private const int Gap = 6;
    private const int EdgeMargin = 24;
    private const int Threshold = 16;

    private static readonly SnapRect Work = new(0, 0, 1920, 1040);

    /// <summary>이미 놓여 있는 위젯 A.</summary>
    private static readonly SnapRect A = new(500, 100, 800, 500);

    private static (int Dx, int Dy) Snap(SnapRect moving, params SnapRect[] others) =>
        WidgetSnap.Offset(moving, others, Work, Gap, EdgeMargin, Threshold);

    private static SnapRect At(int left, int top, int width = 200, int height = 150) => new(left, top, left + width, top + height);

    [Fact]
    public void Beside_another_widget_it_keeps_a_small_gap_and_lines_up_the_tops()
    {
        // A 오른쪽에 조금 떨어지게, 위 끝은 살짝 어긋나게 놓으면
        var (dx, dy) = Snap(At(A.Right + Gap + 5, A.Top + 7), A);
        Assert.Equal((A.Right + Gap, A.Top), (A.Right + Gap + 5 + dx, A.Top + 7 + dy));
    }

    [Fact]
    public void On_the_left_side_it_snaps_too()
    {
        var moving = At(A.Left - Gap - 200 - 9, A.Top - 4);
        var (dx, dy) = Snap(moving, A);
        Assert.Equal((A.Left - Gap - 200, A.Top), (moving.Left + dx, moving.Top + dy));
    }

    [Fact]
    public void Below_another_widget_it_keeps_a_small_gap_and_lines_up_the_left_edges()
    {
        var moving = At(A.Left + 6, A.Bottom + Gap - 8);
        var (dx, dy) = Snap(moving, A);
        Assert.Equal((A.Left, A.Bottom + Gap), (moving.Left + dx, moving.Top + dy));
    }

    [Fact]
    public void Bottom_edges_line_up_when_they_are_closer_than_the_tops()
    {
        // 높이가 다른 위젯을 옆에 놓으면 아래 끝이 더 가까울 때 아래 끝끼리
        var moving = At(A.Right + Gap, A.Bottom - 150 + 5);
        var (_, dy) = Snap(moving, A);
        Assert.Equal(A.Bottom, moving.Bottom + dy);
    }

    [Fact]
    public void Far_from_everything_it_stays_where_it_is() =>
        Assert.Equal((0, 0), Snap(At(1100, 600), A));

    [Fact]
    public void It_does_not_jump_beside_a_widget_that_is_far_above_or_below()
    {
        // A의 오른쪽 옆 자리와 가로 위치는 같지만 한참 아래 → 옆에 붙이지 않는다
        var (dx, _) = Snap(At(A.Right + Gap + 5, 850), A);
        Assert.Equal(0, dx);
    }

    [Fact]
    public void Edges_line_up_even_when_the_widgets_are_apart()
    {
        // 멀리 아래에 있어도 왼쪽 끝은 맞춰진다 (화면 정돈)
        var moving = At(A.Left - 10, 800);
        var (dx, _) = Snap(moving, A);
        Assert.Equal(A.Left, moving.Left + dx);
    }

    [Fact]
    public void Near_the_screen_edge_it_keeps_the_edge_margin()
    {
        var moving = At(Work.Right - EdgeMargin - 200 + 10, EdgeMargin - 6);
        var (dx, dy) = Snap(moving);
        Assert.Equal((Work.Right - EdgeMargin, EdgeMargin), (moving.Right + dx, moving.Top + dy));
    }

    [Fact]
    public void The_closest_spot_wins()
    {
        // A 옆 간격 자리(806)와 A 오른쪽 끝 맞추기(600)가 아니라, 더 가까운 B 왼쪽 끝(815)
        var b = At(815, 700);
        var moving = At(818, A.Top);
        var (dx, _) = Snap(moving, A, b);
        Assert.Equal(815, moving.Left + dx);
    }
}
