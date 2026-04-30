using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class CompareFileSelectionResolverTests
{
    [Fact]
    public void TryResolve_TwoOrMoreFiles_UsesFirstTwoFiles()
    {
        var resolved = CompareFileSelectionResolver.TryResolve(
            new[] { "left.txt", "right.txt", "ignored.txt" },
            secondFile: null,
            out var selection);

        Assert.True(resolved);
        Assert.Equal(new CompareFileSelection("left.txt", "right.txt"), selection);
    }

    [Fact]
    public void TryResolve_OneFileWithSecondFile_UsesSelectedAndSecondFile()
    {
        var resolved = CompareFileSelectionResolver.TryResolve(
            new[] { "left.txt" },
            "right.txt",
            out var selection);

        Assert.True(resolved);
        Assert.Equal(new CompareFileSelection("left.txt", "right.txt"), selection);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryResolve_OneFileWithoutSecondFile_ReturnsFalse(string? secondFile)
    {
        var resolved = CompareFileSelectionResolver.TryResolve(
            new[] { "left.txt" },
            secondFile,
            out _);

        Assert.False(resolved);
    }

    [Fact]
    public void NeedsSecondFile_ReturnsTrueOnlyForSingleSelection()
    {
        Assert.True(CompareFileSelectionResolver.NeedsSecondFile(new[] { "left.txt" }));
        Assert.False(CompareFileSelectionResolver.NeedsSecondFile(Array.Empty<string>()));
        Assert.False(CompareFileSelectionResolver.NeedsSecondFile(new[] { "left.txt", "right.txt" }));
    }
}
