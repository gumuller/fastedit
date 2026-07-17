using FastEdit.ViewModels;

namespace FastEdit.Infrastructure;

public static class UntitledTabNameAllocator
{
    public static string Allocate(
        IEnumerable<EditorTabViewModel> tabs,
        string? preferredName = null)
    {
        var tabList = tabs.ToList();
        var usedNames = tabList
            .Where(tab => string.IsNullOrEmpty(tab.FilePath))
            .Select(tab => tab.FileName)
            .ToHashSet(StringComparer.Ordinal);

        if (!string.IsNullOrEmpty(preferredName) && !usedNames.Contains(preferredName))
            return preferredName;

        var suffix = tabList.Count + 1;
        string candidate;
        do
        {
            candidate = $"Untitled-{suffix++}";
        }
        while (usedNames.Contains(candidate));

        return candidate;
    }
}
