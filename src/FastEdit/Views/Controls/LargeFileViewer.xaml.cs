using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FastEdit.Core.LargeFile;
using FastEdit.Models;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FastEdit.Views.Controls;

/// <summary>
/// Viewer control for multi-GB files backed by LargeFileDocument.
/// Uses custom rendering on a DrawingVisual to avoid ListView virtualization limits.
/// </summary>
public partial class LargeFileViewer : UserControl
{
    private LargeFileDocument? _doc;
    private RenderCanvas? _canvas;
    private readonly LargeFileViewerViewport _viewport = new();
    private double _lineHeight = 16;
    private List<LargeFileDocument.SearchMatch>? _searchMatches;
    private int _currentMatchIndex = -1;
    private CancellationTokenSource? _searchCts;

    private ILineFilterService? _filterService;

    // Filter support
    private IReadOnlyList<LineFilter>? _filters;
    private CancellationTokenSource? _filterScanCts;

    public LargeFileViewer()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public void ShowFindBar(bool focusSearch)
    {
        ToggleFindBar(true);
        if (focusSearch)
        {
            FindBox.Focus();
            FindBox.SelectAll();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ToggleFindBar(true);
            FindBox.Focus();
            FindBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && FindBar.Visibility == Visibility.Visible)
        {
            ToggleFindBar(false);
            e.Handled = true;
        }
        else if (e.Key == Key.G && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ToggleFindBar(true);
            GotoBox.Focus();
            GotoBox.SelectAll();
            e.Handled = true;
        }
        else if (e.OriginalSource is TextBox)
        {
            return;
        }
        else if (e.Key == Key.Down)
        {
            ScrollBy(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            ScrollBy(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.PageDown)
        {
            ScrollBy(_viewport.VisibleLineCount);
            e.Handled = true;
        }
        else if (e.Key == Key.PageUp)
        {
            ScrollBy(-_viewport.VisibleLineCount);
            e.Handled = true;
        }
        else if (e.Key == Key.Home)
        {
            _viewport.MoveToStart();
            UpdateScrollBar();
            Render();
            e.Handled = true;
        }
        else if (e.Key == Key.End)
        {
            _viewport.MoveToEnd();
            UpdateScrollBar();
            Render();
            e.Handled = true;
        }
    }

    private void ToggleFindBar(bool show)
    {
        FindBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) Focus();
    }

    private void CloseFindBar_Click(object sender, RoutedEventArgs e) => ToggleFindBar(false);

    private (int column, int length)? GetCurrentSearchMatchForLine(long lineNumber, string line)
    {
        if (_searchMatches == null ||
            _currentMatchIndex < 0 ||
            _currentMatchIndex >= _searchMatches.Count ||
            string.IsNullOrEmpty(FindBox.Text))
            return null;

        var match = _searchMatches[_currentMatchIndex];
        if (match.LineNumber != lineNumber)
            return null;

        var column = Math.Min(Math.Max(0, match.ColumnInLine), line.Length);
        var length = Math.Min(FindBox.Text.Length, line.Length - column);
        return length > 0 ? (column, length) : null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_canvas == null)
        {
            _canvas = new RenderCanvas(this);
            RenderHost.Child = _canvas;
        }

        // Subscribe to the line-filter service directly (same pattern as EditorHost)
        if (_filterService == null)
        {
            _filterService = App.Services.GetService<ILineFilterService>();
            if (_filterService != null)
            {
                _filterService.FiltersChanged += OnFilterServiceChanged;
                // Apply current state immediately
                OnFilterServiceChanged();
            }
        }

        UpdateMetrics();
        Render();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_filterService != null)
        {
            _filterService.FiltersChanged -= OnFilterServiceChanged;
            _filterService = null;
        }
        _filterScanCts?.Cancel();
        _searchCts?.Cancel();
    }

    private void OnFilterServiceChanged()
    {
        Dispatcher.Invoke(() =>
        {
            if (_filterService == null) return;
            _filters = _filterService.Filters.ToList();

            if (_filterService.ShowOnlyFilteredLines && _filters.Any(f => f.IsEnabled && !string.IsNullOrEmpty(f.Pattern)))
            {
                _ = ShowOnlyFilteredAsync(_filters);
            }
            else
            {
                if (_viewport.IsFiltered) ClearShowOnly();
                Render();
            }
        });
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SetDocument((e.NewValue as EditorTabViewModel)?.LargeFileDoc);
    }

    private void SetDocument(LargeFileDocument? document)
    {
        if (ReferenceEquals(_doc, document))
            return;

        _searchCts?.Cancel();
        _filterScanCts?.Cancel();
        _doc = document;
        _searchMatches = null;
        _currentMatchIndex = -1;
        _viewport.Configure(_doc?.TotalLines ?? 0, _viewport.VisibleLineCount);
        _viewport.SetTopLine(1);
        _viewport.ClearShowOnly();
        UpdateScrollBar();
        UpdateFooter();
        Render();
    }

    private void UpdateMetrics()
    {
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var ft = new FormattedText("Mg", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, FontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        _lineHeight = Math.Ceiling(ft.Height);
        var visibleLines = Math.Max(1, (int)(RenderHost.ActualHeight / _lineHeight));
        _viewport.Configure(_doc?.TotalLines ?? 0, visibleLines);
    }

    private void UpdateScrollBar()
    {
        if (_doc == null)
        {
            VScroll.Minimum = 0;
            VScroll.Maximum = 0;
            VScroll.ViewportSize = 1;
            VScroll.LargeChange = 1;
            VScroll.Value = 0;
            return;
        }

        VScroll.Minimum = 1;
        VScroll.Maximum = _viewport.MaxTopLine;
        VScroll.ViewportSize = _viewport.VisibleLineCount;
        VScroll.LargeChange = Math.Max(1, _viewport.VisibleLineCount - 1);
        VScroll.Value = _viewport.TopLine;
    }

    private long EffectiveLineCount() => _viewport.EffectiveLineCount;

    private long ResolvePhysicalLine(long logicalIndex1Based) =>
        _viewport.ResolvePhysicalLine(logicalIndex1Based);

    private void UpdateFooter()
    {
        if (_doc == null) { FooterText.Text = ""; return; }
        string mode = _viewport.IsFiltered ? $"Filtered: {_viewport.FilteredLineCount:N0} / " : "";
        FooterText.Text = $"{mode}{_doc.TotalLines:N0} lines • {FormatBytes(_doc.FileSize)} • {_doc.EncodingDisplayName} • Read-only (large file viewer)";
    }

    private static string FormatBytes(long b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {u[i]}";
    }

    internal void Render()
    {
        _canvas?.InvalidateVisual();
    }

    private void RenderHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateMetrics();
        UpdateScrollBar();
        Render();
    }

    private void RenderHost_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        int delta = -e.Delta / 120 * 3;
        ScrollBy(delta);
        e.Handled = true;
    }

    private void VScroll_Scroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
    {
        _viewport.SetTopLine((long)Math.Round(e.NewValue));
        UpdateScrollBar();
        Render();
    }

    private void ScrollBy(int deltaLines)
    {
        _viewport.ScrollBy(deltaLines);
        UpdateScrollBar();
        Render();
    }

    private void GotoBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && long.TryParse(GotoBox.Text, out var line))
        {
            GoToLine(line);
            e.Handled = true;
        }
    }

    public void GoToLine(long lineNumber)
    {
        if (_doc == null) return;
        _viewport.GoToPhysicalLine(lineNumber);
        UpdateScrollBar();
        Render();
    }

    private async void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (_searchMatches == null || _searchMatches.Count == 0)
                await RunSearchAsync();
            if (_searchMatches != null && _searchMatches.Count > 0)
            {
                if (shift) FindPrev_Click(sender, e); else FindNext_Click(sender, e);
            }
            e.Handled = true;
        }
        else if (e.Key != Key.LeftShift && e.Key != Key.RightShift)
        {
            _searchMatches = null; // invalidate on text change
            _currentMatchIndex = -1;
            FindStatusText.Text = "";
        }
    }

    private async Task RunSearchAsync()
    {
        if (_doc == null || string.IsNullOrEmpty(FindBox.Text)) return;
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        FindStatusText.Text = "Searching…";
        try
        {
            _searchMatches = await _doc.SearchAsync(
                FindBox.Text,
                CaseSensitiveBox.IsChecked == true,
                maxResults: 100_000,
                onProgress: null,
                ct: _searchCts.Token);
            _currentMatchIndex = -1;
            FindStatusText.Text = $"{_searchMatches.Count:N0} matches";
        }
        catch (OperationCanceledException) { FindStatusText.Text = ""; }
    }

    private async void FindNext_Click(object sender, RoutedEventArgs e)
    {
        if (_searchMatches == null) await RunSearchAsync();
        if (_searchMatches == null || _searchMatches.Count == 0) return;
        _currentMatchIndex = (_currentMatchIndex + 1) % _searchMatches.Count;
        GoToLine(_searchMatches[_currentMatchIndex].LineNumber);
        FindStatusText.Text = $"{_currentMatchIndex + 1} of {_searchMatches.Count:N0}";
    }

    private async void FindPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_searchMatches == null) await RunSearchAsync();
        if (_searchMatches == null || _searchMatches.Count == 0) return;
        _currentMatchIndex = (_currentMatchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
        GoToLine(_searchMatches[_currentMatchIndex].LineNumber);
        FindStatusText.Text = $"{_currentMatchIndex + 1} of {_searchMatches.Count:N0}";
    }

    public void NavigateToNextFilterMatch()
    {
        NavigateFilterMatch(forward: true);
    }

    public void NavigateToPreviousFilterMatch()
    {
        NavigateFilterMatch(forward: false);
    }

    private void NavigateFilterMatch(bool forward)
    {
        if (_doc == null || _filters == null) return;
        var activeFilters = _filters.Where(f => f.IsEnabled && !string.IsNullOrEmpty(f.Pattern) && !f.IsExcluding).ToList();
        if (activeFilters.Count == 0) return;

        long total = _doc.TotalLines;
        long start = _viewport.IsFiltered
            ? Math.Max(1, ResolvePhysicalLine(_viewport.TopLine))
            : _viewport.TopLine;
        long end = forward ? total : 1;
        int step = forward ? 1 : -1;

        // Scan from just after current top line, wrap around if needed.
        for (int pass = 0; pass < 2; pass++)
        {
            long from = pass == 0 ? start + step : (forward ? 1 : total);
            long to = pass == 0 ? end : start;

            for (long ln = from; forward ? ln <= to : ln >= to; ln += step)
            {
                var text = _doc.GetLine(ln);
                foreach (var f in activeFilters)
                {
                    if (f.Matches(text))
                    {
                        GoToLine(ln);
                        return;
                    }
                }
            }
        }
    }

    // Filter integration --------------------------------------------------
    public void ApplyFilters(IReadOnlyList<LineFilter>? filters)
    {
        _filters = filters;
        Render();
    }

    public async Task ShowOnlyFilteredAsync(IReadOnlyList<LineFilter> filters, IProgress<double>? progress = null)
    {
        if (_doc == null) return;
        var activeFilters = filters.Where(f => f.IsEnabled && !string.IsNullOrEmpty(f.Pattern)).ToList();
        if (activeFilters.Count == 0) { ClearShowOnly(); return; }

        _filterScanCts?.Cancel();
        _filterScanCts = new CancellationTokenSource();
        var ct = _filterScanCts.Token;
        _filters = filters;

        FooterText.Text = "Scanning for filter matches… 0%";
        var footerProgress = new Progress<double>(p =>
            FooterText.Text = $"Scanning for filter matches… {p * 100:0}%");

        try
        {
            var result = await _doc.FindMatchingLinesAsync(
                predicate: line => LineMatchesAnyFilter(line, activeFilters),
                maxResults: int.MaxValue,
                onProgress: footerProgress,
                ct: ct);

            _viewport.ShowOnly(result);
            UpdateScrollBar();
            UpdateFooter();
            Render();
        }
        catch (OperationCanceledException)
        {
            UpdateFooter();
        }
    }

    public void ClearShowOnly()
    {
        _viewport.ClearShowOnly();
        UpdateScrollBar();
        UpdateFooter();
        Render();
    }

    private static bool LineMatchesAnyFilter(string line, IReadOnlyList<LineFilter> filters)
    {
        bool included = false;
        bool hasIncluder = false;
        foreach (var f in filters)
        {
            if (!f.IsEnabled || string.IsNullOrEmpty(f.Pattern)) continue;
            bool m = f.Matches(line);
            if (f.IsExcluding)
            {
                if (m) return false;
            }
            else
            {
                hasIncluder = true;
                if (m) included = true;
            }
        }
        return hasIncluder ? included : true;
    }

    private (Brush bg, Brush fg)? GetFilterBrushesFor(string line)
    {
        if (_filters == null) return null;
        foreach (var f in _filters)
        {
            if (!f.IsEnabled || f.IsExcluding || string.IsNullOrEmpty(f.Pattern)) continue;
            if (f.Matches(line))
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(f.BackgroundColor);
                    return (new SolidColorBrush(c), ContrastingTextBrush(c));
                }
                catch { return null; }
            }
        }
        return null;
    }

    private static Brush ContrastingTextBrush(Color bg)
    {
        // WCAG-style relative luminance; pick white text on dark bg and near-black on light bg.
        double r = SRGBtoLinear(bg.R / 255.0);
        double g = SRGBtoLinear(bg.G / 255.0);
        double b = SRGBtoLinear(bg.B / 255.0);
        double L = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        return L > 0.45 ? Brushes.Black : Brushes.White;
    }

    private static double SRGBtoLinear(double c) =>
        c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private sealed class RenderCanvas : FrameworkElement
    {
        private readonly LargeFileViewer _owner;
        public RenderCanvas(LargeFileViewer owner) { _owner = owner; }

        protected override void OnRender(DrawingContext dc)
        {
            var bg = TryGetBrush("EditorBackgroundBrush", Brushes.White);
            var fg = TryGetBrush("EditorForegroundBrush", Brushes.Black);
            var gutter = TryGetBrush("PanelBackgroundBrush", Brushes.LightGray);
            var gutterFg = TryGetBrush("LineNumberForegroundBrush", Brushes.Gray);

            dc.DrawRectangle(bg, null, new Rect(0, 0, ActualWidth, ActualHeight));

            if (_owner._doc == null) return;

            var typeface = new Typeface(_owner.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double lineH = _owner._lineHeight;
            long totalEff = _owner.EffectiveLineCount();
            long totalPhys = _owner._doc.TotalLines;

            // Gutter width based on max physical line number
            var maxFt = new FormattedText(totalPhys.ToString(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, _owner.FontSize, gutterFg, dpi);
            double gutterW = maxFt.Width + 16;

            dc.DrawRectangle(gutter, null, new Rect(0, 0, gutterW, ActualHeight));

            int visible = (int)Math.Ceiling(ActualHeight / lineH) + 1;
            for (int i = 0; i < visible; i++)
            {
                long logical = _owner._viewport.TopLine + i;
                if (logical > totalEff) break;
                long physical = _owner.ResolvePhysicalLine(logical);
                if (physical == 0) break;

                string line = _owner._doc.GetLine(physical);
                double y = i * lineH;

                // Filter highlight: contrasting text color
                var brushes = _owner.GetFilterBrushesFor(line);
                Brush textBrush = fg;
                if (brushes != null)
                {
                    dc.DrawRectangle(brushes.Value.bg, null, new Rect(gutterW, y, ActualWidth - gutterW, lineH));
                    textBrush = brushes.Value.fg;
                }

                var numFt = new FormattedText(physical.ToString(), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, _owner.FontSize, gutterFg, dpi);
                dc.DrawText(numFt, new Point(gutterW - numFt.Width - 6, y));

                var textFt = new FormattedText(line, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, _owner.FontSize, textBrush, dpi);
                var textOrigin = new Point(gutterW + 6, y);
                DrawCurrentSearchMatch(dc, line, physical, textOrigin, typeface, textBrush, dpi, lineH);
                dc.DrawText(textFt, textOrigin);
            }
        }

        private void DrawCurrentSearchMatch(
            DrawingContext dc,
            string line,
            long lineNumber,
            Point textOrigin,
            Typeface typeface,
            Brush textBrush,
            double dpi,
            double lineHeight)
        {
            var match = _owner.GetCurrentSearchMatchForLine(lineNumber, line);
            if (match == null)
                return;

            var prefix = match.Value.column == 0 ? string.Empty : line.Substring(0, match.Value.column);
            var matchText = line.Substring(match.Value.column, match.Value.length);
            var prefixText = new FormattedText(prefix, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, _owner.FontSize, textBrush, dpi);
            var matchFormattedText = new FormattedText(matchText, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, _owner.FontSize, textBrush, dpi);
            var highlightBrush = TryGetBrush("EditorFindHighlightBrush", new SolidColorBrush(Color.FromArgb(120, 255, 200, 0)));
            var x = textOrigin.X + prefixText.WidthIncludingTrailingWhitespace;
            dc.DrawRectangle(highlightBrush, null, new Rect(x, textOrigin.Y, matchFormattedText.WidthIncludingTrailingWhitespace, lineHeight));
        }

        private Brush TryGetBrush(string key, Brush fallback)
        {
            try { return (Brush)(_owner.TryFindResource(key) ?? fallback); } catch { return fallback; }
        }
    }
}
