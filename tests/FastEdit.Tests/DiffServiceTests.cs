using DiffPlex.DiffBuilder.Model;
using FastEdit.Services;
using Xunit;

namespace FastEdit.Tests;

public class DiffServiceTests
{
    private readonly DiffService _sut = new();

    [Fact]
    public void ComputeDiff_IdenticalTexts_ZeroChanges()
    {
        var result = _sut.ComputeDiff("hello\nworld", "hello\nworld");

        Assert.Equal(0, result.ChangeCount);
        Assert.Empty(result.LeftDiffLines);
        Assert.Empty(result.RightDiffLines);
    }

    [Fact]
    public void ComputeDiff_DifferentTexts_DetectsChanges()
    {
        var result = _sut.ComputeDiff("line1\nline2", "line1\nline3");

        Assert.True(result.ChangeCount > 0);
    }

    [Fact]
    public void ComputeDiff_InsertedLine_DetectsInsertion()
    {
        var result = _sut.ComputeDiff("line1\nline2", "line1\nline1.5\nline2");

        Assert.True(result.RightDiffLines.Any(d => d.Type == ChangeType.Inserted));
    }

    [Fact]
    public void ComputeDiff_DeletedLine_DetectsDeletion()
    {
        var result = _sut.ComputeDiff("line1\nline2\nline3", "line1\nline3");

        Assert.True(result.LeftDiffLines.Any(d => d.Type == ChangeType.Deleted));
    }

    [Fact]
    public void ComputeDiff_EmptyTexts_ZeroChanges()
    {
        var result = _sut.ComputeDiff("", "");

        Assert.Equal(0, result.ChangeCount);
    }

    [Fact]
    public void ComputeDiff_LeftEmpty_AllInserted()
    {
        var result = _sut.ComputeDiff("", "new line");

        Assert.True(result.ChangeCount > 0);
        Assert.True(result.RightDiffLines.Count > 0);
    }

    [Fact]
    public void ComputeDiff_RightEmpty_AllDeleted()
    {
        var result = _sut.ComputeDiff("old line", "");

        Assert.True(result.ChangeCount > 0);
        Assert.True(result.LeftDiffLines.Count > 0);
    }

    [Fact]
    public void ComputeDiff_LeftText_MatchesReconstructed()
    {
        var left = "line1\nline2\nline3";
        var right = "line1\nmodified\nline3";
        var result = _sut.ComputeDiff(left, right);

        Assert.Equal(left, result.LeftText);
    }

    [Fact]
    public void ComputeDiff_RightText_MatchesReconstructed()
    {
        var left = "line1\nline2";
        var right = "line1\nline3";
        var result = _sut.ComputeDiff(left, right);

        Assert.Equal(right, result.RightText);
    }

    [Fact]
    public void ComputeDiff_LineNumbers_AreCorrect()
    {
        var result = _sut.ComputeDiff("a\nb\nc", "a\nB\nc");

        var changed = result.LeftDiffLines.FirstOrDefault();
        Assert.Equal(2, changed.LineNum);
    }
}
