using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using FastEdit.Models;

namespace FastEdit.Views.Controls;

public class FilterBackgroundRenderer : IBackgroundRenderer
{
    private readonly TextView _textView;
    private Dictionary<int, LineFilterResult> _lineResults = new();

    public KnownLayer Layer => KnownLayer.Background;

    public FilterBackgroundRenderer(TextView textView)
    {
        _textView = textView;
    }

    public void UpdateResults(Dictionary<int, LineFilterResult> results)
    {
        _lineResults = results;
        _textView.InvalidateLayer(Layer);
    }

    public void ClearResults()
    {
        _lineResults = new();
        _textView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_lineResults.Count == 0) return;

        foreach (var visualLine in textView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            if (!_lineResults.TryGetValue(lineNumber, out var result)) continue;
            if (result.MatchingFilter == null || result.MatchesExclude) continue;

            var color = ParseColor(result.MatchingFilter.BackgroundColor);
            var brush = new SolidColorBrush(Color.FromArgb(45, color.R, color.G, color.B));
            brush.Freeze();

            var lineTop = visualLine.VisualTop - textView.ScrollOffset.Y;
            var rect = new Rect(0, lineTop, textView.ActualWidth, visualLine.Height);
            drawingContext.DrawRectangle(brush, null, rect);
        }
    }

    private static Color ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return Colors.Blue;
        }
    }
}
