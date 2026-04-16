using System.Collections.ObjectModel;
using FastEdit.Models;

namespace FastEdit.Services.Interfaces;

public interface ILineFilterService
{
    ObservableCollection<LineFilter> Filters { get; }
    bool ShowOnlyFilteredLines { get; set; }
    bool HasActiveFilters { get; }
    event Action? FiltersChanged;

    void AddFilter(LineFilter filter);
    void RemoveFilter(LineFilter filter);
    void ClearFilters();
    void EnableAll();
    void DisableAll();
    LineFilterResult EvaluateLine(string lineText);
    void SaveFilters(string path);
    void LoadFilters(string path);
}
