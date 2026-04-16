using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using FastEdit.Models;

namespace FastEdit.Views.Controls;

public class FilterDimTransformer : DocumentColorizingTransformer
{
    private Dictionary<int, LineFilterResult> _lineResults = new();
    private bool _hasActiveFilters;
    private static readonly SolidColorBrush DimBrush;

    static FilterDimTransformer()
    {
        DimBrush = new SolidColorBrush(Color.FromArgb(55, 128, 128, 128));
        DimBrush.Freeze();
    }

    public void UpdateResults(Dictionary<int, LineFilterResult> results, bool hasActiveFilters)
    {
        _lineResults = results;
        _hasActiveFilters = hasActiveFilters;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (!_hasActiveFilters || _lineResults.Count == 0) return;

        var lineNumber = line.LineNumber;

        // Lines matching an exclude filter or not matching any include filter get dimmed
        if (_lineResults.TryGetValue(lineNumber, out var result))
        {
            if (result.MatchesExclude)
                DimLine(line);
            // If it matches an include filter, keep normal (background renderer handles color)
        }
        else
        {
            // No match at all — dim it
            DimLine(line);
        }
    }

    private void DimLine(DocumentLine line)
    {
        if (line.Length == 0) return;
        ChangeLinePart(line.Offset, line.EndOffset, element =>
        {
            element.TextRunProperties.SetForegroundBrush(DimBrush);
        });
    }
}
