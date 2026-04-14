using FastEdit.Helpers;

namespace FastEdit.Tests;

public class LineEndingHelperTests
{
    // --- Detection ---
    [Fact]
    public void Detect_CRLF()
    {
        Assert.Equal(LineEndingType.CRLF, LineEndingHelper.Detect("line1\r\nline2\r\nline3"));
    }

    [Fact]
    public void Detect_LF()
    {
        Assert.Equal(LineEndingType.LF, LineEndingHelper.Detect("line1\nline2\nline3"));
    }

    [Fact]
    public void Detect_CR()
    {
        Assert.Equal(LineEndingType.CR, LineEndingHelper.Detect("line1\rline2\rline3"));
    }

    [Fact]
    public void Detect_Mixed()
    {
        Assert.Equal(LineEndingType.Mixed, LineEndingHelper.Detect("line1\r\nline2\nline3"));
    }

    [Fact]
    public void Detect_Empty_Defaults_To_CRLF()
    {
        Assert.Equal(LineEndingType.CRLF, LineEndingHelper.Detect(""));
    }

    [Fact]
    public void Detect_No_Line_Endings_Defaults_To_CRLF()
    {
        Assert.Equal(LineEndingType.CRLF, LineEndingHelper.Detect("single line"));
    }

    // --- Conversion ---
    [Fact]
    public void Convert_LF_To_CRLF()
    {
        var result = LineEndingHelper.Convert("a\nb\nc", LineEndingType.CRLF);
        Assert.Equal("a\r\nb\r\nc", result);
    }

    [Fact]
    public void Convert_CRLF_To_LF()
    {
        var result = LineEndingHelper.Convert("a\r\nb\r\nc", LineEndingType.LF);
        Assert.Equal("a\nb\nc", result);
    }

    [Fact]
    public void Convert_CRLF_To_CR()
    {
        var result = LineEndingHelper.Convert("a\r\nb\r\nc", LineEndingType.CR);
        Assert.Equal("a\rb\rc", result);
    }

    [Fact]
    public void Convert_Mixed_To_LF()
    {
        var result = LineEndingHelper.Convert("a\r\nb\nc\rd", LineEndingType.LF);
        Assert.Equal("a\nb\nc\nd", result);
    }

    [Fact]
    public void Convert_Preserves_Content()
    {
        var input = "Hello World\r\nFoo Bar\r\nBaz";
        var result = LineEndingHelper.Convert(input, LineEndingType.LF);
        Assert.Equal("Hello World\nFoo Bar\nBaz", result);
    }

    [Fact]
    public void Convert_No_Line_Endings_Returns_Same()
    {
        var result = LineEndingHelper.Convert("no newlines", LineEndingType.LF);
        Assert.Equal("no newlines", result);
    }

    // --- Display String ---
    [Theory]
    [InlineData(LineEndingType.CRLF, "CRLF")]
    [InlineData(LineEndingType.LF, "LF")]
    [InlineData(LineEndingType.CR, "CR")]
    [InlineData(LineEndingType.Mixed, "Mixed")]
    public void ToDisplayString_Returns_Correct_Label(LineEndingType type, string expected)
    {
        Assert.Equal(expected, LineEndingHelper.ToDisplayString(type));
    }

    // --- Round-trip ---
    [Theory]
    [InlineData(LineEndingType.CRLF)]
    [InlineData(LineEndingType.LF)]
    [InlineData(LineEndingType.CR)]
    public void Convert_Then_Detect_Is_Consistent(LineEndingType target)
    {
        var input = "line1\r\nline2\r\nline3";
        var converted = LineEndingHelper.Convert(input, target);
        var detected = LineEndingHelper.Detect(converted);
        Assert.Equal(target, detected);
    }
}
