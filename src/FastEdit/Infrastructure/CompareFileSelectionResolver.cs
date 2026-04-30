namespace FastEdit.Infrastructure;

public static class CompareFileSelectionResolver
{
    public static bool TryResolve(
        IReadOnlyList<string> selectedFiles,
        string? secondFile,
        out CompareFileSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selectedFiles);

        if (selectedFiles.Count >= 2)
        {
            selection = new CompareFileSelection(selectedFiles[0], selectedFiles[1]);
            return true;
        }

        if (selectedFiles.Count == 1 && !string.IsNullOrEmpty(secondFile))
        {
            selection = new CompareFileSelection(selectedFiles[0], secondFile);
            return true;
        }

        selection = default;
        return false;
    }

    public static bool NeedsSecondFile(IReadOnlyList<string> selectedFiles)
    {
        ArgumentNullException.ThrowIfNull(selectedFiles);
        return selectedFiles.Count == 1;
    }
}

public readonly record struct CompareFileSelection(string LeftPath, string RightPath);
