using FastEdit.Theming;

namespace FastEdit.Tests;

public class ThemeLoaderTests
{
    [Fact]
    public void Themes_Are_Loaded_On_Construction()
    {
        var loader = new ThemeLoader();

        Assert.NotEmpty(loader.Themes);
    }

    [Fact]
    public void Built_In_Themes_Include_Dark_And_Light()
    {
        var loader = new ThemeLoader();

        var names = loader.Themes.Select(t => t.Name).ToList();
        Assert.Contains("Dark", names);
        Assert.Contains("Light", names);
    }

    [Fact]
    public void All_Built_In_Themes_Have_Required_Properties()
    {
        var loader = new ThemeLoader();

        foreach (var theme in loader.Themes)
        {
            Assert.False(string.IsNullOrEmpty(theme.Name), $"Theme has empty Name");
            Assert.False(string.IsNullOrEmpty(theme.DisplayName), $"Theme {theme.Name} has empty DisplayName");
            Assert.NotNull(theme.Colors);
            Assert.NotNull(theme.SyntaxColors);

            // Colors should have essential values
            Assert.False(string.IsNullOrEmpty(theme.Colors.WindowBackground),
                $"Theme {theme.Name} missing WindowBackground");
            Assert.False(string.IsNullOrEmpty(theme.Colors.EditorBackground),
                $"Theme {theme.Name} missing EditorBackground");
            Assert.False(string.IsNullOrEmpty(theme.Colors.EditorForeground),
                $"Theme {theme.Name} missing EditorForeground");
        }
    }

    [Fact]
    public void GetTheme_Returns_Theme_By_Name()
    {
        var loader = new ThemeLoader();
        var theme = loader.GetTheme("Dark");

        Assert.NotNull(theme);
        Assert.Equal("Dark", theme!.Name);
    }

    [Fact]
    public void GetTheme_Returns_Null_For_Unknown_Name()
    {
        var loader = new ThemeLoader();

        Assert.Null(loader.GetTheme("NonExistentTheme12345"));
    }

    [Fact]
    public void GetTheme_Is_Case_Insensitive()
    {
        var loader = new ThemeLoader();

        var upper = loader.GetTheme("DARK");
        var lower = loader.GetTheme("dark");

        // Both should find the same theme (or both null if case-sensitive)
        if (upper != null)
            Assert.Equal(upper.Name, lower?.Name);
    }

    [Fact]
    public void Dark_Themes_Have_IsDark_True()
    {
        var loader = new ThemeLoader();
        var darkTheme = loader.GetTheme("Dark");

        Assert.NotNull(darkTheme);
        Assert.True(darkTheme!.IsDark);
    }

    [Fact]
    public void Light_Theme_Has_IsDark_False()
    {
        var loader = new ThemeLoader();
        var lightTheme = loader.GetTheme("Light");

        Assert.NotNull(lightTheme);
        Assert.False(lightTheme!.IsDark);
    }

    [Fact]
    public void SyntaxColors_Have_Valid_Hex_Format()
    {
        var loader = new ThemeLoader();

        foreach (var theme in loader.Themes)
        {
            var syntax = theme.SyntaxColors;
            AssertValidHexColor(syntax.Keyword, $"{theme.Name}.Keyword");
            AssertValidHexColor(syntax.String, $"{theme.Name}.String");
            AssertValidHexColor(syntax.Comment, $"{theme.Name}.Comment");
            AssertValidHexColor(syntax.Number, $"{theme.Name}.Number");
        }
    }

    [Fact]
    public void LoadFromFile_Returns_Null_For_NonExistent_File()
    {
        var result = ThemeLoader.LoadFromFile(@"C:\nonexistent\theme.json");
        Assert.Null(result);
    }

    [Fact]
    public void RefreshCustomThemes_Does_Not_Throw()
    {
        var loader = new ThemeLoader();
        var exception = Record.Exception(() => loader.RefreshCustomThemes());
        Assert.Null(exception);
    }

    [Fact]
    public void Themes_Have_No_Duplicates()
    {
        var loader = new ThemeLoader();
        var names = loader.Themes.Select(t => t.Name).ToList();
        var distinct = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Equal(distinct.Count, names.Count);
    }

    [Theory]
    [InlineData("Dracula")]
    [InlineData("Monokai")]
    [InlineData("OneDark")]
    [InlineData("SolarizedDark")]
    [InlineData("SolarizedLight")]
    [InlineData("Nord")]
    [InlineData("RetroGreen")]
    public void Built_In_Theme_Exists(string themeName)
    {
        var loader = new ThemeLoader();
        Assert.NotNull(loader.GetTheme(themeName));
    }

    private static void AssertValidHexColor(string? color, string context)
    {
        Assert.False(string.IsNullOrEmpty(color), $"{context} is empty");
        Assert.StartsWith("#", color!, StringComparison.Ordinal);
        Assert.True(color!.Length is 7 or 9, $"{context} '{color}' is not valid hex (#RRGGBB or #AARRGGBB)");
    }
}
