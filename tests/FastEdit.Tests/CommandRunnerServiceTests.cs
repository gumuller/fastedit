using System.Diagnostics;
using System.IO;
using System.Text;
using FastEdit.Infrastructure;
using FastEdit.Services;
using Xunit;

namespace FastEdit.Tests;

public class CommandRunnerServiceTests
{
    [Fact]
    public async Task ExecuteCommand_AddsToHistory()
    {
        await using var sut = new CommandRunnerService();

        await StartReadyShellAsync(sut);
        await ExecuteAndWaitAsync(sut, "Write-Output 'hello'");

        Assert.Single(sut.History);
        Assert.Equal("Write-Output 'hello'", sut.History[0]);
    }

    [Fact]
    public async Task History_NavigatesWithoutChangingExistingBehavior()
    {
        await using var sut = new CommandRunnerService();
        await StartReadyShellAsync(sut);
        await ExecuteAndWaitAsync(sut, "Write-Output 'first'");
        await ExecuteAndWaitAsync(sut, "Write-Output 'second'");

        Assert.Equal("Write-Output 'second'", sut.GetPreviousHistoryItem());
        Assert.Equal("Write-Output 'first'", sut.GetPreviousHistoryItem());
        Assert.Null(sut.GetPreviousHistoryItem());
        Assert.Equal("Write-Output 'second'", sut.GetNextHistoryItem());
        Assert.Equal(string.Empty, sut.GetNextHistoryItem());
    }

    [Fact]
    public async Task ExecuteCommand_Whitespace_DoesNotStartShellOrAddHistory()
    {
        await using var sut = new CommandRunnerService();
        var started = false;
        sut.CommandStarted += () => started = true;

        await sut.ExecuteCommandAsync("   ");

        Assert.Empty(sut.History);
        Assert.False(started);
        Assert.False(sut.IsRunning);
    }

    [Fact]
    public async Task SetWorkingDirectory_ValidPath_UpdatesStateAndEvent()
    {
        await using var sut = new CommandRunnerService();
        var tempDirectory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var changed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.WorkingDirectoryChanged += directory => changed.TrySetResult(directory);

        Assert.True(await sut.SetWorkingDirectoryAsync(tempDirectory));

        Assert.Equal(tempDirectory, sut.WorkingDirectory);
        Assert.Equal(tempDirectory, await changed.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task SetWorkingDirectory_InvalidOrNullPath_DoesNotChangeState()
    {
        await using var sut = new CommandRunnerService();
        var original = sut.WorkingDirectory;

        Assert.False(await sut.SetWorkingDirectoryAsync(@"C:\NonExistentDir12345"));
        Assert.False(await sut.SetWorkingDirectoryAsync(null));
        Assert.Equal(original, sut.WorkingDirectory);
    }

    [Fact]
    public async Task SetWorkingDirectory_FilePath_UsesDirectory()
    {
        await using var sut = new CommandRunnerService();
        var tempFile = Path.GetTempFileName();
        try
        {
            Assert.True(await sut.SetWorkingDirectoryAsync(tempFile));
            Assert.Equal(Path.GetDirectoryName(tempFile), sut.WorkingDirectory);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task StartShell_WithDirectory_StartsProcessAndSetsWorkingDirectory()
    {
        await using var sut = new CommandRunnerService();
        var tempDirectory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

        await StartReadyShellAsync(sut, tempDirectory);

        Assert.True(sut.IsRunning);
        Assert.Equal(tempDirectory, sut.WorkingDirectory);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task ExecuteCommand_EmitsOutputAndCompletes()
    {
        await using var sut = new CommandRunnerService();
        var output = new StringBuilder();
        var outputSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.OutputReceived += text =>
        {
            output.Append(text);
            if (output.ToString().Contains("fastedit-async-ok", StringComparison.Ordinal))
                outputSeen.TrySetResult();
        };

        await StartReadyShellAsync(sut);
        var completed = WaitForNextCompletionAsync(sut);
        await sut.ExecuteCommandAsync("Write-Output 'fastedit-async-ok'");

        await Task.WhenAll(
            outputSeen.Task.WaitAsync(TimeSpan.FromSeconds(10)),
            completed);

        Assert.Contains("fastedit-async-ok", output.ToString());
        Assert.DoesNotContain(TerminalOutputFramer.SentinelPrefix, output.ToString());
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task ExecuteCommand_EmitsStderrAndStillCompletes()
    {
        await using var sut = new CommandRunnerService();
        var stderrSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.OutputReceived += text =>
        {
            if (text.Contains("fastedit-stderr", StringComparison.Ordinal))
                stderrSeen.TrySetResult();
        };

        await StartReadyShellAsync(sut);
        var completed = WaitForNextCompletionAsync(sut);
        await sut.ExecuteCommandAsync("Write-Error 'fastedit-stderr'");

        await Task.WhenAll(
            stderrSeen.Task.WaitAsync(TimeSpan.FromSeconds(10)),
            completed);
    }

    [Fact]
    public async Task ExecuteCommand_EmitsUnterminatedStderrWithoutWaitingForShellExit()
    {
        await using var sut = new CommandRunnerService();
        await StartReadyShellAsync(sut);
        var stderrSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.OutputReceived += text =>
        {
            if (text.Contains("fastedit-unterminated-stderr", StringComparison.Ordinal))
                stderrSeen.TrySetResult();
        };

        var completed = WaitForNextCompletionAsync(sut);
        await sut.ExecuteCommandAsync(
            "[Console]::Error.Write('fastedit-unterminated-stderr')");

        await Task.WhenAll(
            stderrSeen.Task.WaitAsync(TimeSpan.FromSeconds(10)),
            completed);
        Assert.True(sut.IsRunning);
    }

    [Fact]
    public async Task ExecuteCommand_EmitsUnterminatedStdoutBeforeLaterStderr()
    {
        await using var sut = new CommandRunnerService();
        await StartReadyShellAsync(sut);
        var output = new StringBuilder();
        sut.OutputReceived += text => output.Append(text);

        var completed = WaitForNextCompletionAsync(sut);
        await sut.ExecuteCommandAsync(
            "[Console]::Out.Write('O1'); [Console]::Out.Flush(); " +
            "Start-Sleep -Milliseconds 200; [Console]::Error.WriteLine('E1')");
        await completed;

        var text = output.ToString();
        Assert.Contains("O1", text);
        Assert.Contains("E1", text);
        Assert.True(
            text.IndexOf("O1", StringComparison.Ordinal) <
            text.IndexOf("E1", StringComparison.Ordinal),
            $"Expected stdout before stderr, but received: {text}");
    }

    [Fact]
    public async Task ExecuteCommand_ConsumesOnlyProtocolSeparatorNewLine()
    {
        await using var sut = new CommandRunnerService();
        await StartReadyShellAsync(sut);
        var output = new StringBuilder();
        sut.OutputReceived += text => output.Append(text);

        await ExecuteAndWaitAsync(sut, "Write-Output 'HELLO'");

        Assert.Equal("HELLO\n", output.ToString());
    }

    [Fact]
    public async Task SetupLikeUserOutput_IsNotSuppressed()
    {
        await using var sut = new CommandRunnerService();
        await StartReadyShellAsync(sut);
        var output = new StringBuilder();
        sut.OutputReceived += text => output.Append(text);

        var completed = WaitForNextCompletionAsync(sut);
        await sut.ExecuteCommandAsync(
            "Write-Output '$OutputEncoding'; " +
            "Write-Output '[Console]::OutputEncoding'; " +
            "[Console]::Error.WriteLine('$ProgressPreference')");
        await completed;

        var text = output.ToString();
        Assert.Contains("$OutputEncoding", text);
        Assert.Contains("[Console]::OutputEncoding", text);
        Assert.Contains("$ProgressPreference", text);
    }

    [Fact]
    public async Task HighVolumeOutput_IsCoalescedWithoutLosingPerStreamOrder()
    {
        await using var sut = new CommandRunnerService();
        await StartReadyShellAsync(sut);
        var output = new StringBuilder();
        var outputEventCount = 0;
        sut.OutputReceived += text =>
        {
            outputEventCount++;
            output.Append(text);
        };

        var completed = WaitForNextCompletionAsync(sut);
        await sut.ExecuteCommandAsync(
            "1..1000 | ForEach-Object { Write-Output \"fastedit-line-$_\" }");
        await completed;

        var allOutput = output.ToString();
        Assert.Contains("fastedit-line-1\n", allOutput);
        Assert.Contains("fastedit-line-1000\n", allOutput);
        Assert.True(outputEventCount < 1000);
    }

    [Fact]
    public async Task StaleSentinel_DoesNotCompleteCommandOrUpdateWorkingDirectory()
    {
        await using var sut = new CommandRunnerService();
        await StartReadyShellAsync(sut);
        var originalDirectory = sut.WorkingDirectory;
        var staleDirectory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(staleDirectory, originalDirectory, StringComparison.OrdinalIgnoreCase))
        {
            staleDirectory = Directory.CreateTempSubdirectory("fastedit-stale-").FullName;
        }

        try
        {
            var markerSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.OutputReceived += text =>
            {
                if (text.Contains("after-stale", StringComparison.Ordinal))
                    markerSeen.TrySetResult();
            };
            var completed = WaitForNextCompletionAsync(sut);
            var staleToken = Guid.NewGuid();

            await sut.ExecuteCommandAsync(
                $"Write-Output '{TerminalOutputFramer.SentinelPrefix}1|{staleToken:N}|{staleDirectory}'; " +
                "Write-Output 'after-stale'; Start-Sleep -Milliseconds 300");

            await markerSeen.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(completed.IsCompleted);
            Assert.Equal(originalDirectory, sut.WorkingDirectory);

            await completed;
            Assert.Equal(originalDirectory, sut.WorkingDirectory);
        }
        finally
        {
            if (!string.Equals(staleDirectory, Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(staleDirectory);
            }
        }
    }

    [Fact]
    public async Task CurrentCommandIdWithWrongToken_DoesNotCompletePrematurely()
    {
        await using var sut = new CommandRunnerService();
        await StartReadyShellAsync(sut);
        var wrongToken = Guid.NewGuid();
        var markerSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.OutputReceived += text =>
        {
            if (text.Contains("after-spoof", StringComparison.Ordinal))
                markerSeen.TrySetResult();
        };
        var completed = WaitForNextCompletionAsync(sut);

        await sut.ExecuteCommandAsync(
            $"Write-Output '{TerminalOutputFramer.SentinelPrefix}2|{wrongToken:N}|{sut.WorkingDirectory}'; " +
            "Write-Output 'after-spoof'; Start-Sleep -Milliseconds 300");

        await markerSeen.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(completed.IsCompleted);
        await completed;
    }

    [Fact]
    public async Task SetWorkingDirectoryAsync_WhileBusy_IsRejectedWithoutDesynchronizingState()
    {
        await using var sut = new CommandRunnerService();
        await StartReadyShellAsync(sut);
        var originalDirectory = sut.WorkingDirectory;
        var newDirectory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var completed = WaitForNextCompletionAsync(sut);

        await sut.ExecuteCommandAsync("Start-Sleep -Milliseconds 300");
        var changed = await sut.SetWorkingDirectoryAsync(newDirectory);

        Assert.False(changed);
        Assert.Equal(originalDirectory, sut.WorkingDirectory);
        await completed;
        Assert.Equal(originalDirectory, sut.WorkingDirectory);
    }

    [Fact]
    public async Task StopCurrentProcessAsync_InterruptsImmediatelyAndCompletesOnce()
    {
        await using var sut = new CommandRunnerService();
        await StartReadyShellAsync(sut);
        var completionCount = 0;
        var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.CommandCompleted += () =>
        {
            completionCount++;
            restarted.TrySetResult();
        };
        await sut.ExecuteCommandAsync("Start-Sleep -Seconds 5");
        var stopwatch = Stopwatch.StartNew();

        await sut.StopCurrentProcessAsync();
        await restarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(100);

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
        Assert.Equal(1, completionCount);
        Assert.True(sut.IsRunning);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task ShutdownAsync_StopsReadersAndProcessWithinBound()
    {
        var sut = new CommandRunnerService();
        await StartReadyShellAsync(sut);
        var stopwatch = Stopwatch.StartNew();

        await sut.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(7));

        stopwatch.Stop();
        Assert.False(sut.IsRunning);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(7));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sut.StartShellAsync());
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_IsIdempotent()
    {
        var sut = new CommandRunnerService();
        await StartReadyShellAsync(sut);

        await sut.DisposeAsync();
        await sut.DisposeAsync();

        Assert.False(sut.IsRunning);
    }

    [Fact]
    public async Task Factory_CreatesFreshRunnerAfterShutdown()
    {
        var factory = new CommandRunnerFactory();
        var first = factory.Create();
        await first.ShutdownAsync();

        var second = factory.Create();
        try
        {
            Assert.NotSame(first, second);
            await second.StartShellAsync();
            Assert.True(second.IsRunning);
        }
        finally
        {
            await second.ShutdownAsync();
        }
    }

    [Fact]
    public void StripAnsiCodes_RemovesEscapeSequencesAndPreservesPlainText()
    {
        Assert.Equal("Red text", CommandRunnerService.StripAnsiCodes("\x1B[31mRed text\x1B[0m"));
        Assert.Equal("plain text", CommandRunnerService.StripAnsiCodes("plain text"));
    }

    private static async Task StartReadyShellAsync(
        CommandRunnerService service,
        string? initialDirectory = null)
    {
        var completed = WaitForNextCompletionAsync(service);
        await service.StartShellAsync(initialDirectory);
        await completed;
    }

    private static async Task ExecuteAndWaitAsync(CommandRunnerService service, string command)
    {
        var completed = WaitForNextCompletionAsync(service);
        await service.ExecuteCommandAsync(command);
        await completed;
    }

    private static Task WaitForNextCompletionAsync(CommandRunnerService service)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action? handler = null;
        handler = () =>
        {
            service.CommandCompleted -= handler;
            completion.TrySetResult();
        };
        service.CommandCompleted += handler;
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
