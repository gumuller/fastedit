using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class TerminalPromptFormatterTests
{
    [Fact]
    public void FormatPrompt_UserProfileDirectory_UsesHomeAlias()
    {
        var prompt = TerminalPromptFormatter.FormatPrompt(
            @"C:\Users\gmuller",
            @"C:\Users\gmuller");

        Assert.Equal("~ ❯ ", prompt);
    }

    [Fact]
    public void FormatPrompt_ChildOfUserProfile_UsesHomeRelativePath()
    {
        var prompt = TerminalPromptFormatter.FormatPrompt(
            @"C:\Users\gmuller\source\repos",
            @"C:\Users\gmuller");

        Assert.Equal(@"~\source\repos ❯ ", prompt);
    }

    [Fact]
    public void FormatPrompt_OutsideUserProfile_UsesFullPath()
    {
        var prompt = TerminalPromptFormatter.FormatPrompt(
            @"D:\work",
            @"C:\Users\gmuller");

        Assert.Equal(@"D:\work ❯ ", prompt);
    }

    [Fact]
    public void FormatPrompt_EmptyWorkingDirectory_ReturnsDefaultPrompt()
    {
        var prompt = TerminalPromptFormatter.FormatPrompt("", @"C:\Users\gmuller");

        Assert.Equal("❯ ", prompt);
    }
}
