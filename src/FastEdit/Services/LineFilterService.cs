using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using FastEdit.Models;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public class LineFilterService : ILineFilterService
{
    public ObservableCollection<LineFilter> Filters { get; } = new();

    private bool _showOnlyFilteredLines;
    public bool ShowOnlyFilteredLines
    {
        get => _showOnlyFilteredLines;
        set
        {
            if (_showOnlyFilteredLines == value) return;
            _showOnlyFilteredLines = value;
            FiltersChanged?.Invoke();
        }
    }

    public bool HasActiveFilters => Filters.Any(f => f.IsEnabled);

    public event Action? FiltersChanged;

    public void AddFilter(LineFilter filter)
    {
        filter.PropertyChanged += (_, _) => FiltersChanged?.Invoke();
        Filters.Add(filter);
        FiltersChanged?.Invoke();
    }

    public void RemoveFilter(LineFilter filter)
    {
        Filters.Remove(filter);
        FiltersChanged?.Invoke();
    }

    public void ClearFilters()
    {
        Filters.Clear();
        FiltersChanged?.Invoke();
    }

    public void EnableAll()
    {
        foreach (var f in Filters) f.IsEnabled = true;
    }

    public void DisableAll()
    {
        foreach (var f in Filters) f.IsEnabled = false;
    }

    public LineFilterResult EvaluateLine(string lineText)
    {
        bool matchesInclude = false;
        bool matchesExclude = false;
        LineFilter? firstMatch = null;

        foreach (var filter in Filters)
        {
            if (!filter.IsEnabled) continue;
            if (!filter.Matches(lineText)) continue;

            if (filter.IsExcluding)
            {
                matchesExclude = true;
            }
            else
            {
                matchesInclude = true;
                firstMatch ??= filter;
            }
        }

        if (!matchesInclude && !matchesExclude)
            return LineFilterResult.NoMatch;

        return new LineFilterResult(matchesInclude, matchesExclude, firstMatch);
    }

    public void SaveFilters(string path)
    {
        var data = Filters.Select(f => new FilterData
        {
            Pattern = f.Pattern,
            IsRegex = f.IsRegex,
            IsCaseSensitive = f.IsCaseSensitive,
            IsExcluding = f.IsExcluding,
            BackgroundColor = f.BackgroundColor,
            IsEnabled = f.IsEnabled
        }).ToList();

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public void LoadFilters(string path)
    {
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<List<FilterData>>(json);
        if (data == null) return;

        Filters.Clear();
        foreach (var d in data)
        {
            AddFilter(new LineFilter
            {
                Pattern = d.Pattern,
                IsRegex = d.IsRegex,
                IsCaseSensitive = d.IsCaseSensitive,
                IsExcluding = d.IsExcluding,
                BackgroundColor = d.BackgroundColor,
                IsEnabled = d.IsEnabled
            });
        }
    }

    private class FilterData
    {
        public string Pattern { get; set; } = "";
        public bool IsRegex { get; set; }
        public bool IsCaseSensitive { get; set; }
        public bool IsExcluding { get; set; }
        public string BackgroundColor { get; set; } = "#4488FF";
        public bool IsEnabled { get; set; } = true;
    }
}
