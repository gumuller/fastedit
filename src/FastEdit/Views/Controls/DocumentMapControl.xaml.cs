using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;

namespace FastEdit.Views.Controls;

/// <summary>
/// A minimap/document map that shows a condensed overview of the document.
/// </summary>
public partial class DocumentMapControl : UserControl
{
    private TextEditor? _editor;
    private bool _isDragging;
    private DrawingVisual? _mapVisual;

    public DocumentMapControl()
    {
        InitializeComponent();
    }

    public void AttachEditor(TextEditor editor)
    {
        if (_editor != null)
        {
            _editor.TextArea.TextView.ScrollOffsetChanged -= OnScrollChanged;
            _editor.TextChanged -= OnTextChanged;
        }

        _editor = editor;
        _editor.TextArea.TextView.ScrollOffsetChanged += OnScrollChanged;
        _editor.TextChanged += OnTextChanged;

        UpdateMap();
    }

    public void DetachEditor()
    {
        if (_editor != null)
        {
            _editor.TextArea.TextView.ScrollOffsetChanged -= OnScrollChanged;
            _editor.TextChanged -= OnTextChanged;
            _editor = null;
        }
        MapCanvas.Children.Clear();
    }

    private void OnScrollChanged(object? sender, EventArgs e) => UpdateViewport();
    private void OnTextChanged(object? sender, EventArgs e) => UpdateMap();

    private void UpdateMap()
    {
        if (_editor == null) return;

        MapCanvas.Children.Clear();

        var doc = _editor.Document;
        if (doc.TextLength == 0) return;

        // Render text at tiny scale
        double scaleFactor = Math.Min(1.0, ActualHeight > 0 ? ActualHeight / Math.Max(1, doc.LineCount * 2.0) : 0.5);
        double fontSize = Math.Max(1.0, Math.Min(3.0, scaleFactor * 10));

        var foreground = TryFindResource("WindowForegroundBrush") as Brush ?? Brushes.Gray;

        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            double y = 0;
            var typeface = new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            int lineCount = doc.LineCount;
            int step = Math.Max(1, lineCount / 500); // limit rendering for very large files

            for (int i = 1; i <= lineCount; i += step)
            {
                var line = doc.GetLineByNumber(i);
                int length = Math.Min(line.Length, 120); // cap line width
                if (length > 0)
                {
                    var text = doc.GetText(line.Offset, length);
                    var indent = 0;
                    foreach (char c in text)
                    {
                        if (c == ' ') indent++;
                        else if (c == '\t') indent += 4;
                        else break;
                    }

                    // Draw a thin colored line representing code
                    double x = indent * fontSize * 0.5;
                    double lineWidth = Math.Max(1, (length - indent) * fontSize * 0.5);
                    ctx.DrawRectangle(foreground, null, new Rect(x, y, lineWidth, Math.Max(1, fontSize * 0.8)));
                }
                y += fontSize;
            }
        }

        var host = new DocumentMapVisualHost(visual);
        MapCanvas.Children.Add(host);
        _mapVisual = visual;

        UpdateViewport();
    }

    private void UpdateViewport()
    {
        if (_editor == null || ActualHeight <= 0) return;

        var doc = _editor.Document;
        if (doc.LineCount == 0) return;

        double totalLines = doc.LineCount;
        double firstVisibleLine = _editor.TextArea.TextView.VerticalOffset / _editor.TextArea.TextView.DefaultLineHeight;
        double visibleLines = _editor.TextArea.TextView.ActualHeight / _editor.TextArea.TextView.DefaultLineHeight;

        double scaleFactor = Math.Min(1.0, ActualHeight / Math.Max(1, totalLines * 2.0));
        double fontSize = Math.Max(1.0, Math.Min(3.0, scaleFactor * 10));
        int step = Math.Max(1, (int)totalLines / 500);

        double top = (firstVisibleLine / step) * fontSize;
        double height = Math.Max(10, (visibleLines / step) * fontSize);

        ViewportIndicator.Margin = new Thickness(0, Math.Max(0, top), 0, 0);
        ViewportIndicator.Height = height;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        CaptureMouse();
        ScrollToPosition(e.GetPosition(this));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
            ScrollToPosition(e.GetPosition(this));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ReleaseMouseCapture();
    }

    private void ScrollToPosition(Point pos)
    {
        if (_editor == null || ActualHeight <= 0) return;

        double fraction = pos.Y / ActualHeight;
        int targetLine = Math.Clamp((int)(fraction * _editor.Document.LineCount), 1, _editor.Document.LineCount);
        _editor.ScrollToLine(targetLine);
    }
}

/// <summary>
/// Hosts a DrawingVisual in a UIElement for Canvas rendering.
/// </summary>
internal class DocumentMapVisualHost : UIElement
{
    private readonly DrawingVisual _visual;

    public DocumentMapVisualHost(DrawingVisual visual)
    {
        _visual = visual;
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _visual;
}
