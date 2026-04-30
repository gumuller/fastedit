using System.Globalization;

namespace FastEdit.Infrastructure;

public static class ByteSizeFormatter
{
    public static string Format(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture) + $" {units[unitIndex]}";
    }
}
