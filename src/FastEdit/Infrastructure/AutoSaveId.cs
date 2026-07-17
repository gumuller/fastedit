using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace FastEdit.Infrastructure;

public static class AutoSaveId
{
    public static string ForFilePath(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var normalizedPath = Path.GetFullPath(filePath)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .ToUpperInvariant();
        return Hash($"file:{normalizedPath}");
    }

    public static string ForUntitled(string stableKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableKey);
        return Hash($"untitled:{stableKey}");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
