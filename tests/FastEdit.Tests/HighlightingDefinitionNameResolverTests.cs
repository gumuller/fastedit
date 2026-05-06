using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class HighlightingDefinitionNameResolverTests
{
    [Theory]
    [InlineData("C#", "C#")]
    [InlineData("TypeScript", "JavaScript")]
    [InlineData("C", "C++")]
    [InlineData("JSON", "Json")]
    [InlineData("SQL", "TSQL")]
    [InlineData("Markdown", "MarkDown")]
    [InlineData("Shell", "Bash")]
    [InlineData("unknown", null)]
    [InlineData("", null)]
    public void Resolve_ReturnsAvalonEditDefinitionName(string language, string? expected)
    {
        Assert.Equal(expected, HighlightingDefinitionNameResolver.Resolve(language));
    }
}
