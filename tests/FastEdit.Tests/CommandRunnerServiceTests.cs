using System.IO;
using FastEdit.Services;
using Xunit;

namespace FastEdit.Tests;

public class CommandRunnerServiceTests
{
    private CommandRunnerService CreateSut() => new();

    // --- History ---

    [Fact]
    public async Task ExecuteCommand_AddsToHistory()
    {
        var sut = CreateSut();
        var output = new List<string>();
        sut.OutputReceived += s => output.Add(s);

        await sut.ExecuteCommandAsync("echo hello");

        Assert.Single(sut.History);
        Assert.Equal("echo hello", sut.History[0]);
    }

    [Fact]
    public async Task History_NavigateUp_ReturnsPrevious()
    {
        var sut = CreateSut();
        await sut.ExecuteCommandAsync("echo first");
        await sut.ExecuteCommandAsync("echo second");

        var prev = sut.GetPreviousHistoryItem();
        Assert.Equal("echo second", prev);

        prev = sut.GetPreviousHistoryItem();
        Assert.Equal("echo first", prev);
    }

    [Fact]
    public async Task History_NavigateDown_ReturnsNext()
    {
        var sut = CreateSut();
        await sut.ExecuteCommandAsync("echo first");
        await sut.ExecuteCommandAsync("echo second");

        sut.GetPreviousHistoryItem(); // "echo second"
        sut.GetPreviousHistoryItem(); // "echo first"

        var next = sut.GetNextHistoryItem();
        Assert.Equal("echo second", next);
    }

    [Fact]
    public void History_NavigateUp_AtBeginning_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(sut.GetPreviousHistoryItem());
    }

    [Fact]
    public async Task History_NavigateDown_AtEnd_ReturnsEmpty()
    {
        var sut = CreateSut();
        await sut.ExecuteCommandAsync("echo test");

        var next = sut.GetNextHistoryItem();
        Assert.Equal(string.Empty, next);
    }

    // --- Working Directory ---

    [Fact]
    public void SetWorkingDirectory_ValidPath_ReturnsTrue()
    {
        var sut = CreateSut();
        var tempDir = Path.GetTempPath();

        Assert.True(sut.SetWorkingDirectory(tempDir));
        Assert.Equal(tempDir, sut.WorkingDirectory);
    }

    [Fact]
    public void SetWorkingDirectory_InvalidPath_ReturnsFalse()
    {
        var sut = CreateSut();
        var original = sut.WorkingDirectory;

        Assert.False(sut.SetWorkingDirectory(@"C:\NonExistentDir12345"));
        Assert.Equal(original, sut.WorkingDirectory);
    }

    [Fact]
    public void SetWorkingDirectory_Null_ReturnsFalse()
    {
        var sut = CreateSut();
        Assert.False(sut.SetWorkingDirectory(null));
    }

    // --- CD Command ---

    [Fact]
    public async Task CdCommand_ChangesWorkingDirectory()
    {
        var sut = CreateSut();
        var tempDir = Path.GetTempPath();
        sut.SetWorkingDirectory(tempDir);

        var output = new List<string>();
        sut.OutputReceived += s => output.Add(s);

        await sut.ExecuteCommandAsync($"cd {tempDir}");

        Assert.Contains(output, s => s.Contains("Changed to:"));
    }

    [Fact]
    public async Task CdCommand_InvalidDir_ShowsError()
    {
        var sut = CreateSut();
        var output = new List<string>();
        sut.OutputReceived += s => output.Add(s);

        await sut.ExecuteCommandAsync("cd C:\\NonExistent12345");

        Assert.Contains(output, s => s.Contains("not found"));
    }

    // --- Command Execution ---

    [Fact]
    public async Task ExecuteCommand_CapturesOutput()
    {
        var sut = CreateSut();
        var output = new List<string>();
        sut.OutputReceived += s => output.Add(s);

        await sut.ExecuteCommandAsync("echo hello world");

        Assert.Contains(output, s => s.Contains("hello world"));
    }

    [Fact]
    public async Task ExecuteCommand_EmptyCommand_ReturnsFalse()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteCommandAsync("  ");
        Assert.False(result);
    }

    [Fact]
    public async Task ExecuteCommand_FiresCommandCompleted()
    {
        var sut = CreateSut();
        bool completed = false;
        sut.CommandCompleted += () => completed = true;

        await sut.ExecuteCommandAsync("echo test");

        Assert.True(completed);
    }

    // --- StripAnsiCodes ---

    [Fact]
    public void StripAnsiCodes_RemovesEscapeSequences()
    {
        var input = "\x1B[31mRed text\x1B[0m";
        var result = CommandRunnerService.StripAnsiCodes(input);
        Assert.Equal("Red text", result);
    }

    [Fact]
    public void StripAnsiCodes_PlainText_Unchanged()
    {
        var result = CommandRunnerService.StripAnsiCodes("plain text");
        Assert.Equal("plain text", result);
    }

    // --- Dispose ---

    [Fact]
    public void Dispose_CalledTwice_NoProblem()
    {
        var sut = CreateSut();
        sut.Dispose();
        sut.Dispose();
    }
}
