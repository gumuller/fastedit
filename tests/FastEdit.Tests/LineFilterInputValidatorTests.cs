using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class LineFilterInputValidatorTests
{
    [Fact]
    public void Validate_TrimsPlainTextPattern()
    {
        var result = LineFilterInputValidator.Validate("  error  ", isRegex: false);

        Assert.True(result.IsValid);
        Assert.Equal("error", result.Pattern);
        Assert.Null(result.ErrorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyPattern_ReturnsValidationError(string? input)
    {
        var result = LineFilterInputValidator.Validate(input, isRegex: false);

        Assert.False(result.IsValid);
        Assert.Equal("Pattern cannot be empty.", result.ErrorMessage);
    }

    [Fact]
    public void Validate_ValidRegex_ReturnsPattern()
    {
        var result = LineFilterInputValidator.Validate("error\\d+", isRegex: true);

        Assert.True(result.IsValid);
        Assert.Equal("error\\d+", result.Pattern);
    }

    [Fact]
    public void Validate_InvalidRegex_ReturnsValidationError()
    {
        var result = LineFilterInputValidator.Validate("[", isRegex: true);

        Assert.False(result.IsValid);
        Assert.StartsWith("Invalid regex:", result.ErrorMessage);
    }
}
