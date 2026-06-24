using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace ClaudeAccountSwitcher.Controls;

/// <summary>
/// 드래그 순서 변경 시 삽입 위치를 가로선(+양끝 삼각형)으로 표시하는 어도너.
/// 대상 행(ListViewItem) 위에 얹혀 그 행의 위/아래 모서리에 선을 그린다.
/// </summary>
public sealed class InsertionAdorner : Adorner
{
    private static readonly Pen LinePen;
    private static readonly Brush CapBrush;

    /// <summary>true 면 대상 행의 위쪽, false 면 아래쪽에 선을 그린다.</summary>
    public bool IsAbove { get; }

    static InsertionAdorner()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)); // Azure 액센트
        brush.Freeze();
        CapBrush = brush;
        LinePen = new Pen(brush, 2);
        LinePen.Freeze();
    }

    public InsertionAdorner(UIElement adornedElement, bool isAbove) : base(adornedElement)
    {
        IsAbove = isAbove;
        IsHitTestVisible = false; // 드롭 히트테스트를 가로막지 않도록
    }

    protected override void OnRender(DrawingContext dc)
    {
        var el = (FrameworkElement)AdornedElement;
        double y = IsAbove ? 0 : el.ActualHeight;
        double w = el.ActualWidth;

        dc.DrawLine(LinePen, new Point(0, y), new Point(w, y));
        const double s = 4;
        dc.DrawGeometry(CapBrush, null, Triangle(0, y, s, pointRight: true));
        dc.DrawGeometry(CapBrush, null, Triangle(w, y, s, pointRight: false));
    }

    private static Geometry Triangle(double x, double y, double s, bool pointRight)
    {
        double dir = pointRight ? 1 : -1;
        var fig = new PathFigure { StartPoint = new Point(x, y - s), IsClosed = true };
        fig.Segments.Add(new LineSegment(new Point(x, y + s), isStroked: true));
        fig.Segments.Add(new LineSegment(new Point(x + dir * s, y), isStroked: true));
        var g = new PathGeometry();
        g.Figures.Add(fig);
        g.Freeze();
        return g;
    }
}
