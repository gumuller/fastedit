using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace FastEdit.Helpers;

/// <summary>
/// Highlights all occurrences of a selected word in the editor.
/// </summary>
public class OccurrenceHighlightRenderer : IBackgroundRenderer
{
    private readonly TextView _textView;
    private string? _highlightWord;
    private Brush _highlightBrush;
    private Pen _highlightPen;
    private readonly List<OccurrenceSegment> _segments = new();

    public KnownLayer Layer => KnownLayer.Selection;

    public int OccurrenceCount => _segments.Count;

    public OccurrenceHighlightRenderer(TextView textView)
    {
        _textView = textView;
        _highlightBrush = CreateBrush(Colors.Yellow);
        _highlightPen = CreatePen(Colors.Orange);
    }

    public void UpdateColors(Color background, Color border)
    {
        _highlightBrush = CreateBrush(background);
        _highlightPen = CreatePen(border);
        _textView.InvalidateLayer(Layer);
    }

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(Color.FromArgb(60, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(Color color)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(120, color.R, color.G, color.B)), 1);
        pen.Freeze();
        return pen;
    }

    public void SetHighlightWord(string? word, TextDocument? document)
    {
        if (_highlightWord == word) return;

        _highlightWord = word;
        _segments.Clear();

        if (!string.IsNullOrEmpty(word) && document != null && word.Length >= 2)
        {
            var escapedWord = Regex.Escape(word);
            var pattern = $@"\b{escapedWord}\b";

            try
            {
                var text = document.Text;
                foreach (Match match in Regex.Matches(text, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                {
                    _segments.Add(new OccurrenceSegment(match.Index, match.Length));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Too slow, skip
            }
        }

        _textView.InvalidateLayer(Layer);
    }

    public void Clear()
    {
        _highlightWord = null;
        _segments.Clear();
        _textView.InvalidateLayer(Layer);
    }

    public int GetNextOccurrence(int currentOffset)
    {
        foreach (var seg in _segments)
        {
            if (seg.Offset > currentOffset)
                return seg.Offset;
        }
        return _segments.Count > 0 ? _segments[0].Offset : -1;
    }

    public int GetPreviousOccurrence(int currentOffset)
    {
        for (int i = _segments.Count - 1; i >= 0; i--)
        {
            if (_segments[i].Offset < currentOffset)
                return _segments[i].Offset;
        }
        return _segments.Count > 0 ? _segments[^1].Offset : -1;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_segments.Count == 0) return;

        var visualLines = textView.VisualLines;
        if (visualLines.Count == 0) return;

        var viewStart = visualLines[0].FirstDocumentLine.Offset;
        var lastLine = visualLines[^1].LastDocumentLine;
        var viewEnd = lastLine.Offset + lastLine.Length;

        foreach (var segment in _segments)
        {
            if (segment.Offset + segment.Length < viewStart) continue;
            if (segment.Offset > viewEnd) break;

            var geoBuilder = new BackgroundGeometryBuilder
            {
                CornerRadius = 2,
                AlignToWholePixels = true
            };

            geoBuilder.AddSegment(textView, segment);
            var geometry = geoBuilder.CreateGeometry();

            if (geometry != null)
            {
                drawingContext.DrawGeometry(_highlightBrush, _highlightPen, geometry);
            }
        }
    }
}

public class OccurrenceSegment : ISegment
{
    public OccurrenceSegment(int offset, int length)
    {
        Offset = offset;
        Length = length;
    }

    public int Offset { get; }
    public int Length { get; }
    public int EndOffset => Offset + Length;
}
