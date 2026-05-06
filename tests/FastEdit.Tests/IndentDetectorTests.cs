using FastEdit.Helpers;

namespace FastEdit.Tests;

public class IndentDetectorTests
{
    [Fact]
    public void Detect_EmptyText_DefaultsToFourSpaces()
    {
        var result = IndentDetector.Detect("");

        Assert.False(result.UseTabs);
        Assert.Equal(4, result.IndentSize);
    }

    [Fact]
    public void Detect_TabIndentedText_ReturnsTabs()
    {
        var result = IndentDetector.Detect("root\n\tchild\n\t\tgrandchild");

        Assert.True(result.UseTabs);
        Assert.Equal(4, result.IndentSize);
    }

    [Fact]
    public void Detect_SpaceIndentedText_ReturnsMostCommonIndentSize()
    {
        var result = IndentDetector.Detect("root\n  child\n    grandchild\n  sibling");

        Assert.False(result.UseTabs);
        Assert.Equal(2, result.IndentSize);
    }
}
