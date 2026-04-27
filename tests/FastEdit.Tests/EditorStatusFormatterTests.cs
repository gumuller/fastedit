using FastEdit.Infrastructure;
using FastEdit.ViewModels;

namespace FastEdit.Tests;

public class EditorStatusFormatterTests
{
    [Fact]
    public void FormatLargeFileViewerStatus_Shows_Compact_Line_Count_And_ReadOnly_Mode()
    {
        var status = EditorStatusFormatter.FormatLargeFileViewerStatus(
            totalLines: 26_000_000,
            fileSizeBytes: 123L * 1024 * 1024,
            encoding: "UTF-8");

        Assert.Equal("Large file viewer: 26M lines, read-only | 123 MB | UTF-8", status);
    }

    [Fact]
    public void FormatLargeFileIndexingStatus_Clamps_Progress()
    {
        Assert.Equal(
            "Indexing large file: app.log (100%)",
            EditorStatusFormatter.FormatLargeFileIndexingStatus("app.log", 2));

        Assert.Equal(
            "Indexing large file: app.log (0%)",
            EditorStatusFormatter.FormatLargeFileIndexingStatus("app.log", -1));
    }

    [Theory]
    [InlineData(FileOpenMode.LargeText, "Large file viewer")]
    [InlineData(FileOpenMode.Binary, "Hex mode")]
    public void FormatTextCommandUnavailable_Explains_Mode(FileOpenMode mode, string expectedText)
    {
        var status = EditorStatusFormatter.FormatTextCommandUnavailable(mode);

        Assert.Contains(expectedText, status);
    }
}
