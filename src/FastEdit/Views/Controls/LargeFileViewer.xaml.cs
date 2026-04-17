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
    private long _topLine = 1;
    private double _lineHeight = 16;
    private int _visibleLineCount = 10;
    private List<LargeFileDocument.SearchMatch>? _searchMatches;
    private int _currentMatchIndex = -1;
    private CancellationTokenSource? _searchCts;

    private ILineFilterService? _filterService;

    // Filter support
    private IReadOnlyList<LineFilter>? _filters;
    private List<long>? _showOnlyLines; // when non-null: showing only matching lines
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
    }

    private void ToggleFindBar(bool show)
    {
        FindBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) Focus();
    }

    private void CloseFindBar_Click(object sender, RoutedEventArgs e) => ToggleFindBar(false);

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
                if (_showOnlyLines != null) ClearShowOnly();
                Render();
            }
        });
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is EditorTabViewModel tab && tab.LargeFileDoc != null)
        {
            _doc = tab.LargeFileDoc;
            _topLine = 1;
            UpdateScrollBar();
            UpdateFooter();
            Render();
        }
    }

    private void UpdateMetrics()
    {
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var ft = new FormattedText("Mg", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, FontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        _lineHeight = Math.Ceiling(ft.Height);
        _visibleLineCount = Math.Max(1, (int)(RenderHost.ActualHeight / _lineHeight));
    }

    private void UpdateScrollBar()
    {
        if (_doc == null) { VScroll.Maximum = 0; return; }
        long total = EffectiveLineCount();
        VScroll.Minimum = 1;
        VScroll.Maximum = Math.Max(1, total);
        VScroll.ViewportSize = _visibleLineCount;
        VScroll.LargeChange = Math.Max(1, _visibleLineCount - 1);
        VScroll.Value = _topLine;
    }

    private long EffectiveLineCount() =>
        _showOnlyLines != null ? _showOnlyLines.Count : (_doc?.TotalLines ?? 0);

    private long ResolvePhysicalLine(long logicalIndex1Based)
    {
        if (_showOnlyLines == null) return logicalIndex1Based;
        long idx = logicalIndex1Based - 1;
        if (idx < 0 || idx >= _showOnlyLines.Count) return 0;
        return _showOnlyLines[(int)idx];
    }

    private void UpdateFooter()
    {
        if (_doc == null) { FooterText.Text = ""; return; }
        string mode = _showOnlyLines != null ? $"Filtered: {_showOnlyLines.Count:N0} / " : "";
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
        _topLine = (long)Math.Round(VScroll.Value);
        ClampTopLine();
        Render();
    }

    private void ScrollBy(int deltaLines)
    {
        _topLine += deltaLines;
        ClampTopLine();
        VScroll.Value = _topLine;
        Render();
    }

    private void ClampTopLine()
    {
        long total = EffectiveLineCount();
        if (total == 0) { _topLine = 1; return; }
        long max = Math.Max(1, total - _visibleLineCount + 1);
        if (_topLine > max) _topLine = max;
        if (_topLine < 1) _topLine = 1;
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
        lineNumber = Math.Max(1, Math.Min(_doc.TotalLines, lineNumber));
        if (_showOnlyLines != null)
        {
            // find index in filtered list
            int idx = _showOnlyLines.BinarySearch(lineNumber);
            if (idx < 0) idx = ~idx;
            _topLine = Math.Max(1, idx + 1);
        }
        else
        {
            _topLine = lineNumber;
        }
        ClampTopLine();
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
        long start = _topLine;
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

            _showOnlyLines = result;
            _topLine = 1;
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
        _showOnlyLines = null;
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
                long logical = _owner._topLine + i;
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
                dc.DrawText(textFt, new Point(gutterW + 6, y));
            }
        }

        private Brush TryGetBrush(string key, Brush fallback)
        {
            try { return (Brush)(_owner.TryFindResource(key) ?? fallback); } catch { return fallback; }
        }
    }
}
