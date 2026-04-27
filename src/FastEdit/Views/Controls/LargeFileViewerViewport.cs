using System;
using System.Collections.Generic;

namespace FastEdit.Views.Controls;

public sealed class LargeFileViewerViewport
{
    public long TotalLineCount { get; private set; }
    public int VisibleLineCount { get; private set; } = 1;
    public long TopLine { get; private set; } = 1;
    public IReadOnlyList<long>? FilteredPhysicalLines { get; private set; }

    public bool IsFiltered => FilteredPhysicalLines != null;
    public int FilteredLineCount => FilteredPhysicalLines?.Count ?? 0;
    public long EffectiveLineCount => IsFiltered ? FilteredLineCount : TotalLineCount;
    public long MaxTopLine => EffectiveLineCount == 0
        ? 1
        : Math.Max(1, EffectiveLineCount - VisibleLineCount + 1);

    public void Configure(long totalLineCount, int visibleLineCount)
    {
        if (totalLineCount < 0)
            throw new ArgumentOutOfRangeException(nameof(totalLineCount));

        TotalLineCount = totalLineCount;
        VisibleLineCount = Math.Max(1, visibleLineCount);
        ClampTopLine();
    }

    public void SetTopLine(long lineNumber)
    {
        TopLine = Clamp(lineNumber);
    }

    public void ScrollBy(long deltaLines)
    {
        if (deltaLines > 0 && TopLine > long.MaxValue - deltaLines)
        {
            TopLine = MaxTopLine;
            return;
        }

        if (deltaLines < 0 && TopLine < long.MinValue - deltaLines)
        {
            TopLine = 1;
            return;
        }

        SetTopLine(TopLine + deltaLines);
    }

    public void MoveToStart() => TopLine = 1;

    public void MoveToEnd() => TopLine = MaxTopLine;

    public void GoToPhysicalLine(long lineNumber)
    {
        if (TotalLineCount == 0)
        {
            TopLine = 1;
            return;
        }

        var target = Math.Max(1, Math.Min(TotalLineCount, lineNumber));
        if (!IsFiltered)
        {
            SetTopLine(target);
            return;
        }

        var index = LowerBound(FilteredPhysicalLines!, target);
        SetTopLine(index + 1L);
    }

    public void ShowOnly(IReadOnlyList<long> physicalLines)
    {
        FilteredPhysicalLines = physicalLines ?? throw new ArgumentNullException(nameof(physicalLines));
        TopLine = 1;
        ClampTopLine();
    }

    public void ClearShowOnly()
    {
        FilteredPhysicalLines = null;
        ClampTopLine();
    }

    public long ResolvePhysicalLine(long logicalLineNumber)
    {
        if (logicalLineNumber < 1 || logicalLineNumber > EffectiveLineCount)
            return 0;

        if (!IsFiltered)
            return logicalLineNumber;

        return FilteredPhysicalLines![(int)(logicalLineNumber - 1)];
    }

    private void ClampTopLine()
    {
        TopLine = Clamp(TopLine);
    }

    private long Clamp(long lineNumber)
    {
        if (lineNumber < 1)
            return 1;

        var maxTopLine = MaxTopLine;
        return lineNumber > maxTopLine ? maxTopLine : lineNumber;
    }

    private static int LowerBound(IReadOnlyList<long> lines, long lineNumber)
    {
        var low = 0;
        var high = lines.Count;

        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (lines[mid] < lineNumber)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }
}
