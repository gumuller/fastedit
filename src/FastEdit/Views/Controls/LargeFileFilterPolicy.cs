using FastEdit.Models;

namespace FastEdit.Views.Controls;

public static class LargeFileFilterPolicy
{
    public static IReadOnlyList<LineFilter> GetActiveFilters(IReadOnlyList<LineFilter>? filters)
    {
        return filters == null
            ? Array.Empty<LineFilter>()
            : filters
                .Where(filter => filter.IsEnabled && !string.IsNullOrEmpty(filter.Pattern))
                .ToList();
    }

    public static bool HasNavigationFilter(IReadOnlyList<LineFilter> activeFilters)
    {
        return activeFilters.Any(filter => !filter.IsExcluding);
    }

    public static bool ShouldShowLine(string line, IReadOnlyList<LineFilter> activeFilters)
    {
        var hasIncludeFilter = false;
        var matchesIncludeFilter = false;

        foreach (var filter in activeFilters)
        {
            var matches = filter.Matches(line);
            if (filter.IsExcluding)
            {
                if (matches)
                    return false;
            }
            else
            {
                hasIncludeFilter = true;
                if (matches)
                    matchesIncludeFilter = true;
            }
        }

        return hasIncludeFilter ? matchesIncludeFilter : true;
    }

    public static bool MatchesNavigationFilter(string line, IReadOnlyList<LineFilter> activeFilters)
    {
        return activeFilters.Any(filter => !filter.IsExcluding && filter.Matches(line));
    }

    public static LineFilter? FirstVisibleLineFilter(string line, IReadOnlyList<LineFilter>? filters)
    {
        LineFilter? includeMatch = null;
        foreach (var filter in GetActiveFilters(filters))
        {
            if (!filter.Matches(line))
                continue;

            if (filter.IsExcluding)
                return null;

            includeMatch ??= filter;
        }

        return includeMatch;
    }

    public static bool TryFindAdjacentMatch(
        IReadOnlyList<long> sortedPhysicalLines,
        long currentLine,
        bool forward,
        out long targetLine)
    {
        targetLine = 0;
        if (sortedPhysicalLines.Count == 0)
            return false;

        if (forward)
        {
            var index = UpperBound(sortedPhysicalLines, currentLine);
            targetLine = sortedPhysicalLines[index == sortedPhysicalLines.Count ? 0 : index];
            return true;
        }

        var previousIndex = LowerBound(sortedPhysicalLines, currentLine) - 1;
        targetLine = sortedPhysicalLines[previousIndex < 0 ? sortedPhysicalLines.Count - 1 : previousIndex];
        return true;
    }

    public static bool TryFindNavigationTarget(
        long totalLines,
        long currentLine,
        IReadOnlyList<long>? sortedFilteredPhysicalLines,
        IReadOnlyList<LineFilter> activeFilters,
        bool forward,
        Func<long, string> getLine,
        out long targetLine)
    {
        targetLine = 0;
        if (!HasNavigationFilter(activeFilters))
            return false;

        if (sortedFilteredPhysicalLines != null &&
            TryFindAdjacentMatch(sortedFilteredPhysicalLines, currentLine, forward, out targetLine))
            return true;

        return TryFindMatchingLineByScan(totalLines, currentLine, activeFilters, forward, getLine, out targetLine);
    }

    private static bool TryFindMatchingLineByScan(
        long totalLines,
        long currentLine,
        IReadOnlyList<LineFilter> activeFilters,
        bool forward,
        Func<long, string> getLine,
        out long targetLine)
    {
        foreach (var lineNumber in EnumerateWrappedLines(totalLines, currentLine, forward))
        {
            if (!MatchesNavigationFilter(getLine(lineNumber), activeFilters))
                continue;

            targetLine = lineNumber;
            return true;
        }

        targetLine = 0;
        return false;
    }

    private static IEnumerable<long> EnumerateWrappedLines(long totalLines, long currentLine, bool forward)
    {
        var step = forward ? 1 : -1;
        for (var pass = 0; pass < 2; pass++)
        {
            var from = pass == 0 ? currentLine + step : forward ? 1 : totalLines;
            var to = pass == 0 ? forward ? totalLines : 1 : currentLine;

            for (var lineNumber = from; IsInRange(lineNumber, to, forward); lineNumber += step)
                yield return lineNumber;
        }
    }

    private static bool IsInRange(long lineNumber, long boundary, bool forward) =>
        forward ? lineNumber <= boundary : lineNumber >= boundary;

    private static int LowerBound(IReadOnlyList<long> values, long target)
    {
        var low = 0;
        var high = values.Count;

        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (values[mid] < target)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }

    private static int UpperBound(IReadOnlyList<long> values, long target)
    {
        var low = 0;
        var high = values.Count;

        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (values[mid] <= target)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }
}
