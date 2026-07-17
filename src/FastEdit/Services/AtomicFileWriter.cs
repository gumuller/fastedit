using FastEdit.Services.Interfaces;
using System.IO;

namespace FastEdit.Services;

internal static class AtomicFileWriter
{
    public static void WriteAllText(IFileSystemService fileSystem, string path, string content)
    {
        var tempPath = CreateTempPath(path);
        try
        {
            fileSystem.WriteAllText(tempPath, content);
            fileSystem.MoveFile(tempPath, path, overwrite: true);
        }
        catch
        {
            if (fileSystem.FileExists(tempPath))
                fileSystem.DeleteFile(tempPath);
            throw;
        }
    }

    public static void WriteAllText(string path, string content)
    {
        var tempPath = CreateTempPath(path);
        try
        {
            File.WriteAllText(tempPath, content);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
    }

    private static string CreateTempPath(string path) =>
        $"{path}.{Guid.NewGuid():N}.tmp";
}
