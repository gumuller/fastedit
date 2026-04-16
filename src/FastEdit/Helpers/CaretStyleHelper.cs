using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace FastEdit.Helpers;

/// <summary>
/// Renders custom caret styles (Block, Underscore, LineThin) as an adorner
/// over the AvalonEdit TextArea, hiding the built-in caret.
/// </summary>
public class CaretStyleAdorner : Adorner
{
    private readonly TextArea _textArea;
    private readonly DispatcherTimer _blinkTimer;
    private bool _caretVisible = true;
    private string _style = "Line"; // Line, Block, Underscore, LineThin

    public string CaretStyle
    {
        get => _style;
        set
        {
            _style = value;
            if (_style == "Line")
            {
                // Restore native caret
                _textArea.Caret.Show();
                Visibility = Visibility.Collapsed;
                _blinkTimer.Stop();
            }
            else
            {
                _textArea.Caret.Hide();
                Visibility = Visibility.Visible;
                _blinkTimer.Start();
            }
            InvalidateVisual();
        }
    }

    public CaretStyleAdorner(TextArea textArea) : base(textArea)
    {
        _textArea = textArea;
        IsHitTestVisible = false;

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _blinkTimer.Tick += (_, _) =>
        {
            _caretVisible = !_caretVisible;
            InvalidateVisual();
        };

        _textArea.Caret.PositionChanged += (_, _) =>
        {
            _caretVisible = true;
            _blinkTimer.Stop();
            _blinkTimer.Start();
            InvalidateVisual();
        };

        _textArea.TextView.ScrollOffsetChanged += (_, _) => InvalidateVisual();
        _textArea.TextView.VisualLinesChanged += (_, _) => InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_style == "Line" || !_caretVisible) return;

        var caret = _textArea.Caret;
        var pos = _textArea.TextView.GetVisualPosition(
            new TextViewPosition(caret.Line, caret.Column),
            VisualYPosition.LineTop);

        pos = _textArea.TextView.TranslatePoint(pos, _textArea);
        var lineHeight = _textArea.TextView.DefaultLineHeight;
        var charWidth = GetCharWidth();

        var brush = _textArea.Foreground ?? Brushes.White;

        switch (_style)
        {
            case "Block":
                drawingContext.DrawRectangle(
                    new SolidColorBrush(Color.FromArgb(100, 
                        ((SolidColorBrush)brush).Color.R,
                        ((SolidColorBrush)brush).Color.G,
                        ((SolidColorBrush)brush).Color.B)),
                    new Pen(brush, 1),
                    new Rect(pos.X, pos.Y, charWidth, lineHeight));
                break;

            case "Underscore":
                var underscoreY = pos.Y + lineHeight - 2;
                drawingContext.DrawRectangle(brush, null,
                    new Rect(pos.X, underscoreY, charWidth, 2));
                break;

            case "LineThin":
                drawingContext.DrawRectangle(brush, null,
                    new Rect(pos.X, pos.Y, 1, lineHeight));
                break;
        }
    }

    private double GetCharWidth()
    {
        var typeface = new Typeface(
            _textArea.FontFamily,
            _textArea.FontStyle,
            _textArea.FontWeight,
            _textArea.FontStretch);
        var formattedText = new FormattedText(
            "M",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            _textArea.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return formattedText.Width;
    }
}
