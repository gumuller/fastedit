using FastEdit.Helpers;

namespace FastEdit.Tests;

public class CompletionHelperTests
{
    [Fact]
    public void GetCompletions_Returns_CSharp_Keywords()
    {
        // "vo" is the prefix we're completing
        var doc = new ICSharpCode.AvalonEdit.Document.TextDocument("public vo");
        var completions = CompletionHelper.GetCompletions("C#", doc, 9);
        Assert.Contains(completions, c => c.Text == "void");
        Assert.Contains(completions, c => c.Text == "volatile");
    }

    [Fact]
    public void GetCompletions_Returns_JavaScript_Keywords()
    {
        var doc = new ICSharpCode.AvalonEdit.Document.TextDocument("fu");
        var completions = CompletionHelper.GetCompletions("JavaScript", doc, 2);
        Assert.Contains(completions, c => c.Text == "function");
    }

    [Fact]
    public void GetCompletions_Returns_Python_Keywords()
    {
        var doc = new ICSharpCode.AvalonEdit.Document.TextDocument("de");
        var completions = CompletionHelper.GetCompletions("Python", doc, 2);
        Assert.Contains(completions, c => c.Text == "def");
    }

    [Fact]
    public void GetCompletions_Returns_Rust_Keywords()
    {
        var doc = new ICSharpCode.AvalonEdit.Document.TextDocument("st");
        var completions = CompletionHelper.GetCompletions("Rust", doc, 2);
        Assert.Contains(completions, c => c.Text == "struct");
        Assert.Contains(completions, c => c.Text == "static");
    }

    [Fact]
    public void GetCompletions_Returns_Go_Keywords()
    {
        var doc = new ICSharpCode.AvalonEdit.Document.TextDocument("fu");
        var completions = CompletionHelper.GetCompletions("Go", doc, 2);
        Assert.Contains(completions, c => c.Text == "func");
    }

    [Fact]
    public void GetCompletions_Includes_Document_Words()
    {
        var doc = new ICSharpCode.AvalonEdit.Document.TextDocument("MyCustomVariable = 42;\nMy");
        var completions = CompletionHelper.GetCompletions("C#", doc, 25);
        Assert.Contains(completions, c => c.Text == "MyCustomVariable");
    }

    [Fact]
    public void GetCompletions_Short_Prefix_Returns_Empty()
    {
        var doc = new ICSharpCode.AvalonEdit.Document.TextDocument("v");
        var completions = CompletionHelper.GetCompletions("C#", doc, 1);
        Assert.Empty(completions);
    }

    [Fact]
    public void GetCompletions_Unknown_Language_Returns_Document_Words_Only()
    {
        var doc = new ICSharpCode.AvalonEdit.Document.TextDocument("CustomWord = 1;\nCu");
        var completions = CompletionHelper.GetCompletions("Unknown", doc, 18);
        Assert.Contains(completions, c => c.Text == "CustomWord");
    }

    [Fact]
    public void GetCompletions_Empty_Document_Returns_Empty()
    {
        var doc = new ICSharpCode.AvalonEdit.Document.TextDocument("");
        var completions = CompletionHelper.GetCompletions("C#", doc, 0);
        Assert.Empty(completions);
    }

    [Fact]
    public void GetCompletions_Does_Not_Return_Exact_Match()
    {
        var doc = new ICSharpCode.AvalonEdit.Document.TextDocument("void");
        var completions = CompletionHelper.GetCompletions("C#", doc, 4);
        Assert.DoesNotContain(completions, c => c.Text == "void");
    }

    [Fact]
    public void GetCompletions_SQL_Keywords()
    {
        var doc = new ICSharpCode.AvalonEdit.Document.TextDocument("SE");
        var completions = CompletionHelper.GetCompletions("SQL", doc, 2);
        Assert.Contains(completions, c => c.Text == "SELECT");
    }

    [Fact]
    public void GetCompletions_PowerShell_Keywords()
    {
        var doc = new ICSharpCode.AvalonEdit.Document.TextDocument("fo");
        var completions = CompletionHelper.GetCompletions("PowerShell", doc, 2);
        Assert.Contains(completions, c => c.Text == "foreach");
    }
}
