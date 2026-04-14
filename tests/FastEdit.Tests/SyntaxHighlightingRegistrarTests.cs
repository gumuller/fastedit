using FastEdit.Helpers;

namespace FastEdit.Tests;

public class SyntaxHighlightingRegistrarTests
{
    [Fact]
    public void RegisterAll_Does_Not_Throw()
    {
        // Should not throw even if called multiple times
        SyntaxHighlightingRegistrar.RegisterAll();
        SyntaxHighlightingRegistrar.RegisterAll();
    }

    [Fact]
    public void RegisterAll_Registers_YAML()
    {
        SyntaxHighlightingRegistrar.RegisterAll();
        var definition = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("YAML");
        Assert.NotNull(definition);
        Assert.Equal("YAML", definition.Name);
    }

    [Fact]
    public void RegisterAll_Registers_Bash()
    {
        SyntaxHighlightingRegistrar.RegisterAll();
        var definition = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("Bash");
        Assert.NotNull(definition);
    }

    [Fact]
    public void RegisterAll_Registers_Dockerfile()
    {
        SyntaxHighlightingRegistrar.RegisterAll();
        var definition = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("Dockerfile");
        Assert.NotNull(definition);
    }

    [Fact]
    public void RegisterAll_Registers_Rust()
    {
        SyntaxHighlightingRegistrar.RegisterAll();
        var definition = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("Rust");
        Assert.NotNull(definition);
    }

    [Fact]
    public void RegisterAll_Registers_Go()
    {
        SyntaxHighlightingRegistrar.RegisterAll();
        var definition = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("Go");
        Assert.NotNull(definition);
    }

    [Fact]
    public void RegisterAll_Registers_TOML()
    {
        SyntaxHighlightingRegistrar.RegisterAll();
        var definition = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("TOML");
        Assert.NotNull(definition);
    }

    [Fact]
    public void RegisterAll_Registers_INI()
    {
        SyntaxHighlightingRegistrar.RegisterAll();
        var definition = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("INI");
        Assert.NotNull(definition);
    }

    [Fact]
    public void All_Seven_Custom_Languages_Registered()
    {
        SyntaxHighlightingRegistrar.RegisterAll();
        var mgr = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance;

        var customLanguages = new[] { "YAML", "Bash", "Dockerfile", "Rust", "Go", "TOML", "INI" };
        foreach (var lang in customLanguages)
        {
            Assert.NotNull(mgr.GetDefinition(lang));
        }
    }
}
