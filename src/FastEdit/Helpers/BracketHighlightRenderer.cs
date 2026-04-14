using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace FastEdit.Helpers;

/// <summary>
/// Highlights matching bracket pairs in the editor.
/// </summary>
public class BracketHighlightRenderer : IBackgroundRenderer
{
    private static readonly Brush DefaultBrush = new SolidColorBrush(Color.FromArgb(60, 150, 150, 150));
    private readonly TextEditor _editor;
    private BracketSearchResult? _result;

    public BracketHighlightRenderer(TextEditor editor)
    {
        _editor = editor;
        DefaultBrush.Freeze();
    }

    public Brush HighlightBrush { get; set; } = DefaultBrush;

    public KnownLayer Layer => KnownLayer.Selection;

    public void SetHighlight(BracketSearchResult? result)
    {
        if (_result != result)
        {
            _result = result;
            _editor.TextArea.TextView.InvalidateLayer(Layer);
        }
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_result == null) return;

        var builder = new BackgroundGeometryBuilder
        {
            CornerRadius = 1,
            AlignToWholePixels = true
        };

        builder.AddSegment(textView, new TextSegment { StartOffset = _result.OpeningOffset, Length = 1 });
        var openGeo = builder.CreateGeometry();
        if (openGeo != null)
            drawingContext.DrawGeometry(HighlightBrush, null, openGeo);

        builder = new BackgroundGeometryBuilder
        {
            CornerRadius = 1,
            AlignToWholePixels = true
        };
        builder.AddSegment(textView, new TextSegment { StartOffset = _result.ClosingOffset, Length = 1 });
        var closeGeo = builder.CreateGeometry();
        if (closeGeo != null)
            drawingContext.DrawGeometry(HighlightBrush, null, closeGeo);
    }
}

public class BracketSearchResult
{
    public int OpeningOffset { get; set; }
    public int ClosingOffset { get; set; }
}

public static class BracketSearcher
{
    private static readonly Dictionary<char, char> OpenToClose = new()
    {
        { '(', ')' }, { '[', ']' }, { '{', '}' }, { '<', '>' }
    };

    private static readonly Dictionary<char, char> CloseToOpen = new()
    {
        { ')', '(' }, { ']', '[' }, { '}', '{' }, { '>', '<' }
    };

    public static BracketSearchResult? FindMatchingBracket(TextDocument document, int offset)
    {
        if (offset <= 0 || offset > document.TextLength) return null;

        // Check character before caret
        var charBefore = document.GetCharAt(offset - 1);
        if (OpenToClose.TryGetValue(charBefore, out var closeChar))
            return SearchForward(document, offset - 1, charBefore, closeChar);

        if (CloseToOpen.TryGetValue(charBefore, out var openChar))
            return SearchBackward(document, offset - 1, charBefore, openChar);

        // Check character at caret
        if (offset < document.TextLength)
        {
            var charAt = document.GetCharAt(offset);
            if (OpenToClose.TryGetValue(charAt, out closeChar))
                return SearchForward(document, offset, charAt, closeChar);
            if (CloseToOpen.TryGetValue(charAt, out openChar))
                return SearchBackward(document, offset, charAt, openChar);
        }

        return null;
    }

    private static BracketSearchResult? SearchForward(TextDocument doc, int openPos, char open, char close)
    {
        int depth = 1;
        for (int i = openPos + 1; i < doc.TextLength; i++)
        {
            var ch = doc.GetCharAt(i);
            if (ch == open) depth++;
            else if (ch == close) depth--;
            if (depth == 0)
                return new BracketSearchResult { OpeningOffset = openPos, ClosingOffset = i };
        }
        return null;
    }

    private static BracketSearchResult? SearchBackward(TextDocument doc, int closePos, char close, char open)
    {
        int depth = 1;
        for (int i = closePos - 1; i >= 0; i--)
        {
            var ch = doc.GetCharAt(i);
            if (ch == close) depth++;
            else if (ch == open) depth--;
            if (depth == 0)
                return new BracketSearchResult { OpeningOffset = i, ClosingOffset = closePos };
        }
        return null;
    }
}
