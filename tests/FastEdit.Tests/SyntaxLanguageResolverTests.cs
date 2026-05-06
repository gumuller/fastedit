using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class SyntaxLanguageResolverTests
{
    [Theory]
    [InlineData("test.cs", "C#")]
    [InlineData("test.mjs", "JavaScript")]
    [InlineData("test.tsx", "TypeScript")]
    [InlineData("test.pyw", "Python")]
    [InlineData("test.hpp", "C++")]
    [InlineData("test.h", "C")]
    [InlineData("test.jsonc", "JSON")]
    [InlineData("test.markdown", "Markdown")]
    [InlineData("test.psm1", "PowerShell")]
    [InlineData("test.cmd", "Batch")]
    [InlineData("test.unknown", "")]
    public void Resolve_UsesExtensionMap(string fileName, string expected)
    {
        Assert.Equal(expected, SyntaxLanguageResolver.Resolve(fileName));
    }

    [Theory]
    [InlineData("Dockerfile", "Dockerfile")]
    [InlineData("Containerfile", "Dockerfile")]
    [InlineData("Makefile", "Makefile")]
    [InlineData(".gitignore", "INI")]
    [InlineData(".env.production", "INI")]
    [InlineData("CMakeLists.txt", "CMake")]
    public void Resolve_UsesFileNameBeforeExtension(string fileName, string expected)
    {
        Assert.Equal(expected, SyntaxLanguageResolver.Resolve(fileName));
    }
}
