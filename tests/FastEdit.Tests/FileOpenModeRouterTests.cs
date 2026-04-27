using FastEdit.Core.FileAnalysis;
using FastEdit.ViewModels;

namespace FastEdit.Tests;

public class FileOpenModeRouterTests
{
    [Fact]
    public void SelectOpenMode_BinaryAnalysis_Wins_Over_LargeFileSize()
    {
        var analysis = new BinaryAnalysisResult { IsBinary = true };

        var mode = FileOpenModeRouter.SelectOpenMode(
            EditorTabViewModel.LargeFileThresholdBytes + 1,
            analysis);

        Assert.Equal(FileOpenMode.Binary, mode);
    }

    [Fact]
    public void SelectOpenMode_NonBinary_BelowThreshold_Uses_TextMode()
    {
        var analysis = new BinaryAnalysisResult { IsBinary = false };

        var mode = FileOpenModeRouter.SelectOpenMode(
            EditorTabViewModel.LargeFileThresholdBytes - 1,
            analysis);

        Assert.Equal(FileOpenMode.Text, mode);
    }

    [Fact]
    public void SelectOpenMode_NonBinary_AtThreshold_Uses_LargeTextMode()
    {
        var analysis = new BinaryAnalysisResult { IsBinary = false };

        var mode = FileOpenModeRouter.SelectOpenMode(
            EditorTabViewModel.LargeFileThresholdBytes,
            analysis);

        Assert.Equal(FileOpenMode.LargeText, mode);
    }

    [Fact]
    public void SelectOpenMode_NullAnalysis_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => FileOpenModeRouter.SelectOpenMode(0, null!));
    }

    [Fact]
    public void SelectTextMode_NegativeSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FileOpenModeRouter.SelectTextMode(-1));
    }
}
