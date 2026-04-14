using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Document;

namespace FastEdit.Helpers;

/// <summary>
/// Draws vertical indentation guide lines in the editor.
/// </summary>
public class IndentGuideRenderer : IBackgroundRenderer
{
    private readonly TextView _textView;
    private Pen _guidePen;
    private int _tabSize = 4;

    public KnownLayer Layer => KnownLayer.Background;

    public IndentGuideRenderer(TextView textView)
    {
        _textView = textView;
        _guidePen = CreatePen(Colors.Gray);
    }

    public int TabSize
    {
        get => _tabSize;
        set => _tabSize = Math.Max(1, value);
    }

    public void UpdateColor(Color color)
    {
        _guidePen = CreatePen(color);
        _textView.InvalidateLayer(Layer);
    }

    private static Pen CreatePen(Color color)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)), 1);
        pen.DashStyle = DashStyles.Dot;
        pen.Freeze();
        return pen;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView.Document == null) return;

        var charWidth = textView.WideSpaceWidth;
        if (charWidth <= 0) return;

        foreach (var visualLine in textView.VisualLines)
        {
            var line = visualLine.FirstDocumentLine;
            if (line == null) continue;

            var indentLevel = GetIndentLevel(textView.Document, line);

            for (int level = 1; level < indentLevel; level++)
            {
                var x = Math.Round(level * _tabSize * charWidth - textView.ScrollOffset.X) + 0.5;
                if (x < 0) continue;

                var top = visualLine.VisualTop - textView.ScrollOffset.Y;
                var bottom = top + visualLine.Height;

                drawingContext.DrawLine(_guidePen, new Point(x, top), new Point(x, bottom));
            }
        }
    }

    private int GetIndentLevel(TextDocument document, DocumentLine line)
    {
        var text = document.GetText(line.Offset, Math.Min(line.Length, 200));
        int spaces = 0;

        foreach (var ch in text)
        {
            if (ch == ' ') spaces++;
            else if (ch == '\t') spaces += _tabSize;
            else break;
        }

        return (spaces / _tabSize) + 1;
    }
}
