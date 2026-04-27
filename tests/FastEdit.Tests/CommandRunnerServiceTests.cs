using System.IO;
using System.Text;
using FastEdit.Services;
using Xunit;

namespace FastEdit.Tests;

public class CommandRunnerServiceTests
{
    private CommandRunnerService CreateSut() => new();

    // --- History ---

    [Fact]
    public void ExecuteCommand_AddsToHistory()
    {
        var sut = CreateSut();
        sut.ExecuteCommand("echo hello");

        Assert.Single(sut.History);
        Assert.Equal("echo hello", sut.History[0]);
        sut.Dispose();
    }

    [Fact]
    public void History_NavigateUp_ReturnsPrevious()
    {
        var sut = CreateSut();
        sut.ExecuteCommand("echo first");
        sut.ExecuteCommand("echo second");

        var prev = sut.GetPreviousHistoryItem();
        Assert.Equal("echo second", prev);

        prev = sut.GetPreviousHistoryItem();
        Assert.Equal("echo first", prev);
        sut.Dispose();
    }

    [Fact]
    public void History_NavigateDown_ReturnsNext()
    {
        var sut = CreateSut();
        sut.ExecuteCommand("echo first");
        sut.ExecuteCommand("echo second");

        sut.GetPreviousHistoryItem(); // "echo second"
        sut.GetPreviousHistoryItem(); // "echo first"

        var next = sut.GetNextHistoryItem();
        Assert.Equal("echo second", next);
        sut.Dispose();
    }

    [Fact]
    public void History_NavigateUp_AtBeginning_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(sut.GetPreviousHistoryItem());
    }

    [Fact]
    public void History_NavigateDown_AtEnd_ReturnsEmpty()
    {
        var sut = CreateSut();
        sut.ExecuteCommand("echo test");

        var next = sut.GetNextHistoryItem();
        Assert.Equal(string.Empty, next);
        sut.Dispose();
    }

    [Fact]
    public void ExecuteCommand_Whitespace_DoesNotStartShellOrAddHistory()
    {
        var sut = CreateSut();
        var started = false;
        sut.CommandStarted += () => started = true;

        sut.ExecuteCommand("   ");

        Assert.Empty(sut.History);
        Assert.False(started);
        Assert.False(sut.IsRunning);
    }

    // --- Working Directory ---

    [Fact]
    public void SetWorkingDirectory_ValidPath_ReturnsTrue()
    {
        var sut = CreateSut();
        var tempDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

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

    [Fact]
    public void SetWorkingDirectory_FilePath_UsesDirectory()
    {
        var sut = CreateSut();
        var tempFile = Path.GetTempFileName();
        try
        {
            Assert.True(sut.SetWorkingDirectory(tempFile));
            Assert.Equal(Path.GetDirectoryName(tempFile), sut.WorkingDirectory);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- Shell Lifecycle ---

    [Fact]
    public void StartShell_StartsProcess()
    {
        var sut = CreateSut();
        sut.StartShell();

        Assert.True(sut.IsRunning);
        sut.Dispose();
    }

    [Fact]
    public void StartShell_WithDirectory_SetsWorkingDirectory()
    {
        var sut = CreateSut();
        var tempDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        sut.StartShell(tempDir);

        Assert.True(sut.IsRunning);
        Assert.Equal(tempDir, sut.WorkingDirectory);
        sut.Dispose();
    }

    [Fact]
    public void StopCurrentProcess_RestartShell()
    {
        var sut = CreateSut();
        sut.StartShell();
        Assert.True(sut.IsRunning);

        sut.StopCurrentProcess();
        // Should have restarted
        Assert.True(sut.IsRunning);
        sut.Dispose();
    }

    [Fact]
    public async Task ExecuteCommand_EmitsOutputAndCompletes()
    {
        var sut = CreateSut();
        try
        {
            var output = new StringBuilder();
            var outputSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var commandCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var commandSent = false;

            sut.OutputReceived += text =>
            {
                output.Append(text);
                if (output.ToString().Contains("fastedit-async-ok"))
                    outputSeen.TrySetResult();
            };
            sut.CommandCompleted += () =>
            {
                if (commandSent)
                    commandCompleted.TrySetResult();
            };

            sut.StartShell();
            commandSent = true;
            sut.ExecuteCommand("Write-Output 'fastedit-async-ok'");

            await Task.WhenAll(outputSeen.Task, commandCompleted.Task)
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("fastedit-async-ok", output.ToString());
            Assert.False(sut.IsBusy);
        }
        finally
        {
            sut.Dispose();
        }
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

    [Fact]
    public void Dispose_StopsRunningShell()
    {
        var sut = CreateSut();
        sut.StartShell();
        Assert.True(sut.IsRunning);

        sut.Dispose();
        // After dispose, IsRunning should be false (process killed or disposed)
        Assert.False(sut.IsRunning);
    }
}
