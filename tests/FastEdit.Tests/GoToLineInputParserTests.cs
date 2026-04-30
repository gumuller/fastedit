using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class GoToLineInputParserTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("42", 42)]
    public void TryParse_PositiveInteger_ReturnsLineNumber(string input, int expected)
    {
        var parsed = GoToLineInputParser.TryParse(input, out var lineNumber);

        Assert.True(parsed);
        Assert.Equal(expected, lineNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void TryParse_InvalidInput_ReturnsFalse(string? input)
    {
        var parsed = GoToLineInputParser.TryParse(input, out _);

        Assert.False(parsed);
    }
}
