using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FastEdit.ViewModels;

namespace FastEdit.Views.Controls;

public partial class HexEditorControl : UserControl
{
    private readonly Typeface _typeface;
    private readonly double _lineHeight;
    private readonly double _charWidth;
    private const double HexFontSize = 14;

    private EditorTabViewModel? _viewModel;
    private long _scrollPosition;
    private int _visibleRows;

    // Selection and editing
    private long _selectedOffset = -1;
    private bool _editingHighNibble = true;

    // Cached brushes for performance
    private Brush? _offsetBrush;
    private Brush? _bytesBrush;
    private Brush? _asciiBrush;
    private Brush? _nullBrush;
    private Brush? _backgroundBrush;
    private Brush? _selectionBrush;
    private Brush? _modifiedBrush;

    public HexEditorControl()
    {
        InitializeComponent();

        _typeface = new Typeface(new FontFamily("Cascadia Code, Consolas, Courier New"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        // Calculate character metrics
        var formattedText = new FormattedText(
            "W", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            _typeface, HexFontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        _charWidth = formattedText.Width;
        _lineHeight = formattedText.Height + 4;

        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnSizeChanged;

        // Subscribe to theme changes
        Loaded += (s, e) => RefreshBrushes();
    }

    private void RefreshBrushes()
    {
        _offsetBrush = FindBrush("HexOffsetForegroundBrush");
        _bytesBrush = FindBrush("HexBytesForegroundBrush");
        _asciiBrush = FindBrush("HexAsciiForegroundBrush");
        _nullBrush = FindBrush("HexNullByteForegroundBrush");
        _backgroundBrush = FindBrush("EditorBackgroundBrush");
        _selectionBrush = FindBrush("EditorSelectionBackgroundBrush");
        _modifiedBrush = FindBrush("HexModifiedBackgroundBrush");
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _viewModel = e.NewValue as EditorTabViewModel;
        UpdateScrollBar();
        RefreshBrushes();
        RenderHex();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _visibleRows = (int)(HexCanvas.ActualHeight / _lineHeight) + 1;
        UpdateScrollBar();
        RenderHex();
    }

    private void UpdateScrollBar()
    {
        if (_viewModel?.ByteBuffer == null) return;

        var totalRows = (_viewModel.ByteBuffer.Length + _viewModel.BytesPerRow - 1) / _viewModel.BytesPerRow;
        VerticalScrollBar.Maximum = Math.Max(0, totalRows - _visibleRows);
        VerticalScrollBar.ViewportSize = _visibleRows;
    }

    private void ScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _scrollPosition = (long)e.NewValue;
        RenderHex();
    }

    private void HexCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Scroll 3 lines per wheel notch
        var delta = e.Delta > 0 ? -3 : 3;
        var newValue = Math.Max(0, Math.Min(VerticalScrollBar.Maximum, VerticalScrollBar.Value + delta));
        VerticalScrollBar.Value = newValue;
        e.Handled = true;
    }

    private void HexCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();

        if (_viewModel?.ByteBuffer == null) return;

        var pos = e.GetPosition(HexCanvas);
        var bytesPerRow = _viewModel.BytesPerRow;

        // Layout calculations (must match RenderHex)
        double offsetWidth = _charWidth * 10;

        // Check if click is in hex area
        if (pos.X < offsetWidth) return;

        // Calculate which row was clicked
        int row = (int)(pos.Y / _lineHeight);

        // Calculate which byte in the row
        double hexAreaX = pos.X - offsetWidth;
        int byteInRow = (int)(hexAreaX / (_charWidth * 3));

        // Account for extra space every 8 bytes
        int groups = byteInRow / 8;
        byteInRow = (int)((hexAreaX - groups * _charWidth) / (_charWidth * 3));

        if (byteInRow < 0 || byteInRow >= bytesPerRow) return;

        // Calculate absolute offset
        long offset = (_scrollPosition + row) * bytesPerRow + byteInRow;

        if (offset >= 0 && offset < _viewModel.ByteBuffer.Length)
        {
            _selectedOffset = offset;
            _editingHighNibble = true;
            RenderHex();
        }

        e.Handled = true;
    }

    private void HexCanvas_KeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel?.ByteBuffer == null || _selectedOffset < 0) return;

        var buffer = _viewModel.ByteBuffer;
        var bytesPerRow = _viewModel.BytesPerRow;

        // Handle hex input
        int nibble = -1;
        if (e.Key >= Key.D0 && e.Key <= Key.D9)
            nibble = e.Key - Key.D0;
        else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            nibble = e.Key - Key.NumPad0;
        else if (e.Key >= Key.A && e.Key <= Key.F)
            nibble = 10 + (e.Key - Key.A);

        if (nibble >= 0)
        {
            byte currentByte = buffer.GetByte(_selectedOffset);
            byte newByte;

            if (_editingHighNibble)
            {
                newByte = (byte)((nibble << 4) | (currentByte & 0x0F));
                buffer.SetByte(_selectedOffset, newByte);
                _editingHighNibble = false;
            }
            else
            {
                newByte = (byte)((currentByte & 0xF0) | nibble);
                buffer.SetByte(_selectedOffset, newByte);
                _editingHighNibble = true;

                // Move to next byte
                if (_selectedOffset < buffer.Length - 1)
                    _selectedOffset++;
            }

            RenderHex();
            e.Handled = true;
            return;
        }

        // Handle navigation
        switch (e.Key)
        {
            case Key.Left:
                if (_selectedOffset > 0)
                    _selectedOffset--;
                _editingHighNibble = true;
                break;
            case Key.Right:
                if (_selectedOffset < buffer.Length - 1)
                    _selectedOffset++;
                _editingHighNibble = true;
                break;
            case Key.Up:
                if (_selectedOffset >= bytesPerRow)
                    _selectedOffset -= bytesPerRow;
                _editingHighNibble = true;
                break;
            case Key.Down:
                if (_selectedOffset + bytesPerRow < buffer.Length)
                    _selectedOffset += bytesPerRow;
                _editingHighNibble = true;
                break;
            case Key.PageUp:
                _selectedOffset = Math.Max(0, _selectedOffset - _visibleRows * bytesPerRow);
                _editingHighNibble = true;
                break;
            case Key.PageDown:
                _selectedOffset = Math.Min(buffer.Length - 1, _selectedOffset + _visibleRows * bytesPerRow);
                _editingHighNibble = true;
                break;
            case Key.Home:
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    _selectedOffset = 0;
                else
                    _selectedOffset = (_selectedOffset / bytesPerRow) * bytesPerRow;
                _editingHighNibble = true;
                break;
            case Key.End:
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    _selectedOffset = buffer.Length - 1;
                else
                    _selectedOffset = Math.Min(buffer.Length - 1, ((_selectedOffset / bytesPerRow) + 1) * bytesPerRow - 1);
                _editingHighNibble = true;
                break;
            default:
                return;
        }

        // Ensure selected byte is visible
        EnsureOffsetVisible(_selectedOffset);
        RenderHex();
        e.Handled = true;
    }

    private void EnsureOffsetVisible(long offset)
    {
        if (_viewModel == null) return;

        var bytesPerRow = _viewModel.BytesPerRow;
        long row = offset / bytesPerRow;

        if (row < _scrollPosition)
        {
            VerticalScrollBar.Value = row;
        }
        else if (row >= _scrollPosition + _visibleRows - 1)
        {
            VerticalScrollBar.Value = row - _visibleRows + 2;
        }
    }

    private void RenderHex()
    {
        HexCanvas.Children.Clear();

        if (_viewModel?.ByteBuffer == null) return;
        if (HexCanvas.ActualWidth <= 0 || HexCanvas.ActualHeight <= 0) return;

        // Ensure brushes are loaded
        if (_offsetBrush == null) RefreshBrushes();

        var bytesPerRow = _viewModel.BytesPerRow;
        var startOffset = _scrollPosition * bytesPerRow;

        // Layout calculations
        double offsetWidth = _charWidth * 10;
        double hexWidth = _charWidth * (bytesPerRow * 3 + 2);
        double asciiStart = offsetWidth + hexWidth + 20;

        var rows = _viewModel.ByteBuffer.GetRows(startOffset, _visibleRows + 1, bytesPerRow);

        // Use a single DrawingVisual for better performance
        var drawingVisual = new DrawingVisual();
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        using (var dc = drawingVisual.RenderOpen())
        {
            // Draw background
            dc.DrawRectangle(_backgroundBrush, null, new Rect(0, 0, HexCanvas.ActualWidth, HexCanvas.ActualHeight));

            double y = 0;
            foreach (var row in rows)
            {
                if (y > HexCanvas.ActualHeight) break;

                // Draw offset
                var offsetText = CreateFormattedText(row.OffsetText + ":", _offsetBrush!, pixelsPerDip);
                dc.DrawText(offsetText, new Point(4, y));

                // Draw hex bytes
                double x = offsetWidth;
                for (int i = 0; i < row.Bytes.Length; i++)
                {
                    long byteOffset = row.Offset + i;
                    var b = row.Bytes[i];

                    // Check if this byte is selected
                    bool isSelected = byteOffset == _selectedOffset;

                    // Check if this byte is modified
                    bool isModified = _viewModel?.ByteBuffer?.IsModified(byteOffset) ?? false;

                    // Draw selection/modification background
                    if (isSelected)
                    {
                        dc.DrawRectangle(_selectionBrush, null, new Rect(x - 1, y, _charWidth * 2 + 2, _lineHeight));
                    }
                    else if (isModified)
                    {
                        dc.DrawRectangle(_modifiedBrush, null, new Rect(x - 1, y, _charWidth * 2 + 2, _lineHeight));
                    }

                    var brush = b == 0x00 ? _nullBrush! : _bytesBrush!;
                    if (isSelected)
                        brush = Brushes.White;

                    var hexByte = CreateFormattedText(b.ToString("X2"), brush, pixelsPerDip);
                    dc.DrawText(hexByte, new Point(x, y));

                    x += _charWidth * 3;
                    if ((i + 1) % 8 == 0)
                        x += _charWidth;
                }

                // Draw ASCII
                var asciiText = CreateFormattedText(row.AsciiText, _asciiBrush!, pixelsPerDip);
                dc.DrawText(asciiText, new Point(asciiStart, y));

                y += _lineHeight;
            }
        }

        // Add the visual to a host
        var host = new VisualHost(drawingVisual);
        HexCanvas.Children.Add(host);
    }

    private FormattedText CreateFormattedText(string text, Brush foreground, double pixelsPerDip)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface,
            HexFontSize,
            foreground,
            pixelsPerDip);
    }

    private Brush FindBrush(string resourceKey)
    {
        return FindResource(resourceKey) as Brush ?? Brushes.White;
    }
}

// Helper class to host DrawingVisual in Canvas
public class VisualHost : FrameworkElement
{
    private readonly Visual _visual;

    public VisualHost(Visual visual)
    {
        _visual = visual;
        AddVisualChild(visual);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index)
    {
        return _visual;
    }
}
