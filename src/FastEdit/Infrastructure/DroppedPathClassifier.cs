using FastEdit.Services.Interfaces;

namespace FastEdit.Infrastructure;

public static class DroppedPathClassifier
{
    public static IReadOnlyList<DroppedPathAction> Classify(
        IEnumerable<string> paths,
        IFileSystemService fileSystemService)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(fileSystemService);

        var actions = new List<DroppedPathAction>();
        foreach (var path in paths)
        {
            if (fileSystemService.FileExists(path))
            {
                actions.Add(new DroppedPathAction(path, DroppedPathKind.File));
            }
            else if (fileSystemService.DirectoryExists(path))
            {
                actions.Add(new DroppedPathAction(path, DroppedPathKind.Directory));
            }
            else
            {
                actions.Add(new DroppedPathAction(path, DroppedPathKind.Unsupported));
            }
        }

        return actions;
    }
}

public sealed record DroppedPathAction(string Path, DroppedPathKind Kind);

public enum DroppedPathKind
{
    File,
    Directory,
    Unsupported
}
