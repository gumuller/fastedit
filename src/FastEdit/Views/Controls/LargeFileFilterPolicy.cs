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
