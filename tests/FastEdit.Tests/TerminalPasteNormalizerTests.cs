using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class TerminalPasteNormalizerTests
{
    [Theory]
    [InlineData("one\r\ntwo", "one two")]
    [InlineData("one\rtwo", "one two")]
    [InlineData("one\ntwo", "one two")]
    [InlineData("  one\ntwo  ", "one two")]
    public void NormalizeSingleLine_ReplacesLineBreaksAndTrims(string input, string expected)
    {
        Assert.Equal(expected, TerminalPasteNormalizer.NormalizeSingleLine(input));
    }
}
