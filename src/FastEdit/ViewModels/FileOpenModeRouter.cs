using FastEdit.Core.FileAnalysis;

namespace FastEdit.ViewModels;

public static class FileOpenModeRouter
{
    public static FileOpenMode SelectOpenMode(long fileSizeBytes, BinaryAnalysisResult analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        return analysis.IsBinary
            ? FileOpenMode.Binary
            : SelectTextMode(fileSizeBytes);
    }

    public static FileOpenMode SelectTextMode(long fileSizeBytes)
    {
        if (fileSizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(fileSizeBytes));

        return fileSizeBytes >= EditorTabViewModel.LargeFileThresholdBytes
            ? FileOpenMode.LargeText
            : FileOpenMode.Text;
    }
}
