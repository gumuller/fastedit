using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace FastEdit.Views.Controls;

/// <summary>
/// Adorner that draws a vertical line indicator showing where a dragged tab will be dropped.
/// </summary>
public class TabDropAdorner : Adorner
{
    private double _insertionX;
    private readonly double _height;

    public TabDropAdorner(UIElement adornedElement)
        : base(adornedElement)
    {
        IsHitTestVisible = false;
        _height = adornedElement.RenderSize.Height;
    }

    public void UpdatePosition(double x)
    {
        _insertionX = x;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0, 122, 204)), 2);
        pen.Freeze();
        drawingContext.DrawLine(pen, new Point(_insertionX, 0), new Point(_insertionX, _height));

        // Draw small triangle at top
        var triangleSize = 5.0;
        var triangle = new StreamGeometry();
        using (var ctx = triangle.Open())
        {
            ctx.BeginFigure(new Point(_insertionX - triangleSize, 0), true, true);
            ctx.LineTo(new Point(_insertionX + triangleSize, 0), false, false);
            ctx.LineTo(new Point(_insertionX, triangleSize), false, false);
        }
        triangle.Freeze();
        drawingContext.DrawGeometry(pen.Brush, null, triangle);
    }
}
