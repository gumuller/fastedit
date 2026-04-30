using System.Globalization;
using FastEdit.ViewModels;

namespace FastEdit.Infrastructure;

public static class EditorStatusFormatter
{
    public static string FormatTabStatus(EditorTabViewModel tab)
    {
        return tab.Mode switch
        {
            FileOpenMode.Binary => $"Hex Mode - {tab.FileSize:N0} bytes",
            FileOpenMode.LargeText => FormatLargeFileViewerStatus(
                tab.LargeFileDoc?.TotalLines,
                tab.FileSize,
                tab.Encoding),
            _ => FormatTextStatus(tab)
        };
    }

    public static string FormatLargeFileIndexingStatus(string fileName, double progress)
    {
        var boundedProgress = Math.Clamp(progress, 0, 1);
        var percent = (int)Math.Round(boundedProgress * 100);
        return $"Indexing large file: {fileName} ({percent}%)";
    }

    public static string FormatLargeFileViewerStatus(long? totalLines, long fileSizeBytes, string encoding)
    {
        var lineCount = totalLines.HasValue ? FormatCompactCount(totalLines.Value) : "indexing";
        return $"Large file viewer: {lineCount} lines, read-only | {ByteSizeFormatter.Format(fileSizeBytes)} | {encoding}";
    }

    public static string FormatTextCommandUnavailable(FileOpenMode mode)
    {
        return mode switch
        {
            FileOpenMode.LargeText => "Text editing commands are unavailable in Large file viewer (read-only).",
            FileOpenMode.Binary => "Text editing commands are unavailable in Hex mode.",
            _ => "Text editing command unavailable."
        };
    }

    private static string FormatTextStatus(EditorTabViewModel tab)
    {
        var status = $"Ln {tab.Line}, Col {tab.Column} | {tab.Encoding}";
        var gate = EditorFeatureGatePolicy.Create(tab.Mode, tab.FileSize);
        return gate.StatusMessage == null ? status : $"{gate.StatusMessage} | {status}";
    }

    private static string FormatCompactCount(long value)
    {
        if (value >= 1_000_000_000)
            return FormatScaled(value, 1_000_000_000, "B");

        if (value >= 1_000_000)
            return FormatScaled(value, 1_000_000, "M");

        if (value >= 1_000)
            return FormatScaled(value, 1_000, "K");

        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string FormatScaled(long value, double divisor, string suffix)
    {
        var scaled = value / divisor;
        return scaled.ToString("0.#", CultureInfo.InvariantCulture) + suffix;
    }

}
