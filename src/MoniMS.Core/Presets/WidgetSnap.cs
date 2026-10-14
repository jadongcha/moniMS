namespace MoniMS.Core.Presets;

/// <summary>화면 픽셀 사각형 (오른쪽·아래는 포함하지 않음).</summary>
public readonly record struct SnapRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public SnapRect Offset(int dx, int dy) => new(Left + dx, Top + dy, Right + dx, Bottom + dy);

    public SnapRect Deflate(int inset) => new(Left + inset, Top + inset, Right - inset, Bottom - inset);
}

/// <summary>
/// 위젯 자석: 위젯을 끌어 옮길 때 다른 위젯이나 화면 가장자리 가까이 오면 가지런히 붙인다.
/// - 다른 위젯 옆·위·아래: <c>gap</c>만큼 띄워서 붙인다 (그 위젯과 세로·가로로 겹칠 때만)
/// - 다른 위젯과 위·아래·왼쪽·오른쪽 끝 맞추기 (예: 나란히 놓으면 위 끝이 같아진다)
/// - 화면(작업 영역) 가장자리: <c>edgeMargin</c>만큼 띄워서
/// 가로와 세로는 따로 정하고, 각각 <c>threshold</c> 안에서 가장 가까운 자리로 간다.
/// </summary>
public static class WidgetSnap
{
    /// <param name="moving">옮기는 위젯이 지금 가려는 자리 (보이는 카드 기준).</param>
    /// <param name="others">다른 위젯들 (보이는 카드 기준).</param>
    /// <param name="workArea">위젯이 있는 모니터의 작업 영역 (작업표시줄 제외).</param>
    /// <returns><paramref name="moving"/>을 얼마나 옮기면 붙는지. 붙일 곳이 없으면 (0, 0).</returns>
    public static (int Dx, int Dy) Offset(SnapRect moving, IReadOnlyList<SnapRect> others, SnapRect workArea,
        int gap, int edgeMargin, int threshold)
    {
        var w = moving.Width;
        var h = moving.Height;

        var xs = new List<int> { workArea.Left + edgeMargin, workArea.Right - edgeMargin - w };
        var ys = new List<int> { workArea.Top + edgeMargin, workArea.Bottom - edgeMargin - h };
        foreach (var o in others)
        {
            // 끝 맞추기: 왼쪽끼리·오른쪽끼리, 위끼리·아래끼리
            xs.Add(o.Left);
            xs.Add(o.Right - w);
            ys.Add(o.Top);
            ys.Add(o.Bottom - h);
            // 옆에 붙이기: 세로로 겹칠 때만 (멀리 위·아래에 있는 위젯 옆 자리로 끌려가지 않게)
            if (moving.Top < o.Bottom + threshold && moving.Bottom > o.Top - threshold)
            {
                xs.Add(o.Right + gap);
                xs.Add(o.Left - gap - w);
            }
            // 위·아래에 붙이기: 가로로 겹칠 때만
            if (moving.Left < o.Right + threshold && moving.Right > o.Left - threshold)
            {
                ys.Add(o.Bottom + gap);
                ys.Add(o.Top - gap - h);
            }
        }
        return (Nearest(xs, moving.Left, threshold), Nearest(ys, moving.Top, threshold));
    }

    /// <summary>후보 중 지금 값에서 <paramref name="threshold"/> 안이면서 가장 가까운 곳까지의 거리. 없으면 0.</summary>
    private static int Nearest(List<int> candidates, int current, int threshold)
    {
        var best = 0;
        var bestDistance = threshold + 1;
        foreach (var c in candidates)
        {
            var d = Math.Abs(c - current);
            if (d < bestDistance)
            {
                best = c - current;
                bestDistance = d;
            }
        }
        return best;
    }
}
