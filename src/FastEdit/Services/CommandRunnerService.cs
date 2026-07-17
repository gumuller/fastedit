using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using FastEdit.Infrastructure;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

/// <summary>
/// Manages a persistent PowerShell session. Notifications are delivered serially in
/// observed arrival order; ordering between stdout and stderr depends on reader arrival.
/// </summary>
public sealed class CommandRunnerService : ICommandRunner
{
    private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ForcedExitTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReaderExitTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly object _stateLock = new();
    private readonly object _shutdownLock = new();
    private readonly object _notificationLock = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _notificationSignal = new(0);
    private readonly List<string> _commandHistory = new();
    private readonly LinkedList<RunnerNotification> _notifications = new();
    private readonly Task _notificationTask;

    private Process? _shellProcess;
    private CancellationTokenSource? _readerCancellation;
    private Task? _outputReaderTask;
    private Task? _errorReaderTask;
    private TerminalOutputFramer? _outputFramer;
    private TerminalOutputFramer? _errorFramer;
    private TaskCompletionSource? _shellReadyCompletion;
    private Task? _shutdownTask;
    private string _workingDirectory;
    private int _historyIndex = -1;
    private int _shellGeneration;
    private int _commandId;
    private int _expectedCommandId;
    private Guid _expectedCommandToken;
    private bool _isReady = true;
    private bool _disposeStarted;
    private bool _notificationsComplete;

    public event Action<string>? OutputReceived;
    public event Action<string>? WorkingDirectoryChanged;
    public event Action? CommandStarted;
    public event Action? CommandCompleted;

    public CommandRunnerService()
    {
        _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _notificationTask = DeliverNotificationsAsync();
    }

    public string WorkingDirectory
    {
        get
        {
            lock (_stateLock)
                return _workingDirectory;
        }
    }

    public IReadOnlyList<string> History
    {
        get
        {
            lock (_stateLock)
                return _commandHistory.ToArray();
        }
    }

    public bool IsRunning
    {
        get
        {
            Process? process;
            lock (_stateLock)
                process = _shellProcess;

            try
            {
                return process != null && !process.HasExited;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public bool IsBusy
    {
        get
        {
            lock (_stateLock)
                return !_isReady;
        }
    }

    public async Task StartShellAsync(
        string? initialDirectory = null,
        CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await StartShellCoreAsync(initialDirectory, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        await StartShellAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            Process? process;
            int commandId;
            Guid commandToken;
            lock (_stateLock)
            {
                process = _shellProcess;
                if (process == null || HasExited(process))
                {
                    QueueOutput("Shell is not running.\r\n");
                    return;
                }

                if (!_isReady)
                {
                    QueueOutput("A command is already running.\r\n");
                    return;
                }

                _commandHistory.Add(command);
                _historyIndex = _commandHistory.Count;
                _isReady = false;
                commandId = ++_commandId;
                _expectedCommandId = commandId;
                commandToken = Guid.NewGuid();
                _expectedCommandToken = commandToken;
            }

            QueueNotification(() => CommandStarted?.Invoke());
            SendRawCommand(process, command);
            SendRawCommand(
                process,
                $"Write-Output \"`n{TerminalOutputFramer.SentinelPrefix}{commandId}|{commandToken:N}|$((Get-Location).Path)\"");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public string? GetPreviousHistoryItem()
    {
        lock (_stateLock)
        {
            if (_historyIndex <= 0)
                return null;

            _historyIndex--;
            return _commandHistory[_historyIndex];
        }
    }

    public string? GetNextHistoryItem()
    {
        lock (_stateLock)
        {
            if (_historyIndex < _commandHistory.Count - 1)
            {
                _historyIndex++;
                return _commandHistory[_historyIndex];
            }

            _historyIndex = _commandHistory.Count;
            return string.Empty;
        }
    }

    public async Task StopCurrentProcessAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            lock (_stateLock)
            {
                if (_isReady)
                    return;

                _isReady = false;
                _expectedCommandId = 0;
                _expectedCommandToken = Guid.Empty;
            }

            await StopShellCoreAsync(
                    "stop current command",
                    cancellationToken,
                    force: true)
                .ConfigureAwait(false);
            QueueOutput("\r\n[Process interrupted]\r\n");
            await StartShellCoreAsync(initialDirectory: null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<bool> SetWorkingDirectoryAsync(
        string? directory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(directory))
            return false;

        if (File.Exists(directory))
            directory = Path.GetDirectoryName(directory);

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return false;

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Process? process;
            lock (_stateLock)
            {
                if (_disposeStarted)
                    return false;

                if (!_isReady)
                {
                    QueueOutput("[Working directory change ignored while a command is running.]\r\n");
                    return false;
                }

                _workingDirectory = directory;
                process = _shellProcess;
            }

            if (process != null && !HasExited(process))
                SendRawCommand(process, $"Set-Location '{EscapePath(directory)}'");

            QueueNotification(() => WorkingDirectoryChanged?.Invoke(directory));
            return true;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        Task shutdownTask;
        lock (_shutdownLock)
        {
            _shutdownTask ??= ShutdownCoreAsync();
            shutdownTask = _shutdownTask;
        }

        return shutdownTask.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(DefaultShutdownTimeout);
        await ShutdownAsync(timeout.Token).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public static string StripAnsiCodes(string text)
    {
        return Regex.Replace(text, @"\x1B\[[0-9;]*[a-zA-Z]", "");
    }

    private async Task StartShellCoreAsync(string? initialDirectory, CancellationToken cancellationToken)
    {
        Process? existingProcess;
        Task? existingReadyTask;
        lock (_stateLock)
        {
            existingProcess = _shellProcess;
            existingReadyTask = _shellReadyCompletion?.Task;
        }

        if (existingProcess != null && !HasExited(existingProcess))
        {
            if (existingReadyTask != null)
            {
                try
                {
                    await existingReadyTask.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    await StopShellCoreAsync(
                            "recover failed shell startup",
                            CancellationToken.None,
                            force: true)
                        .ConfigureAwait(false);
                    throw;
                }
                catch (OperationCanceledException)
                {
                    await StopShellCoreAsync(
                            "recover failed shell startup",
                            CancellationToken.None,
                            force: true)
                        .ConfigureAwait(false);
                    throw;
                }
            }
            return;
        }

        if (existingProcess != null)
            await StopShellCoreAsync(
                    "replace exited shell",
                    cancellationToken,
                    force: false)
                .ConfigureAwait(false);

        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            lock (_stateLock)
                _workingDirectory = initialDirectory;
        }

        var process = Process.Start(CreateStartInfo());
        if (process == null)
        {
            QueueOutput("Failed to start shell.\r\n");
            return;
        }

        var readerCancellation = new CancellationTokenSource();
        int generation;
        int initialCommandId;
        Guid initialCommandToken;
        TaskCompletionSource shellReadyCompletion;
        string workingDirectory;
        lock (_stateLock)
        {
            generation = ++_shellGeneration;
            _shellProcess = process;
            _readerCancellation = readerCancellation;
            _outputFramer = new TerminalOutputFramer();
            _errorFramer = new TerminalOutputFramer(
                parseSentinels: false,
                emitPartialChunks: true);
            _isReady = false;
            shellReadyCompletion =
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _shellReadyCompletion = shellReadyCompletion;
            initialCommandId = ++_commandId;
            _expectedCommandId = initialCommandId;
            initialCommandToken = Guid.NewGuid();
            _expectedCommandToken = initialCommandToken;
            workingDirectory = _workingDirectory;
            _outputReaderTask = ReadOutputStreamAsync(
                process.StandardOutput,
                generation,
                readerCancellation.Token);
            _errorReaderTask = ReadErrorStreamAsync(
                process.StandardError,
                generation,
                readerCancellation.Token);
        }

        SendRawCommand(process, "$OutputEncoding = [System.Text.Encoding]::UTF8");
        SendRawCommand(process, "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8");
        SendRawCommand(process, "$ProgressPreference = 'SilentlyContinue'");
        SendRawCommand(process, $"Set-Location '{EscapePath(workingDirectory)}'");
        SendRawCommand(
            process,
            $"Write-Output \"{TerminalOutputFramer.SentinelPrefix}{initialCommandId}|{initialCommandToken:N}|$((Get-Location).Path)\"");

        try
        {
            await shellReadyCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await StopShellCoreAsync(
                    "recover failed shell startup",
                    CancellationToken.None,
                    force: true)
                .ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            await StopShellCoreAsync(
                    "recover failed shell startup",
                    CancellationToken.None,
                    force: true)
                .ConfigureAwait(false);
            throw;
        }
    }

    private ProcessStartInfo CreateStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FindPowerShell(),
            Arguments = "-NoProfile -NoLogo -NonInteractive -Command -",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = WorkingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        return startInfo;
    }

    private async Task ReadOutputStreamAsync(
        StreamReader reader,
        int generation,
        CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        try
        {
            int charactersRead;
            while ((charactersRead = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                ProcessOutputChunk(generation, new string(buffer, 0, charactersRead));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException ex) when (!IsDisposing)
        {
            Trace.TraceWarning("Command runner output stream failed: {0}", ex.Message);
        }
        catch (InvalidOperationException ex) when (!IsDisposing)
        {
            Trace.TraceWarning("Command runner output stream failed: {0}", ex.Message);
        }
        catch (IOException ex) when (!IsDisposing)
        {
            Trace.TraceWarning("Command runner output stream failed: {0}", ex.Message);
        }
        finally
        {
            CompleteOutput(generation);
        }
    }

    private async Task ReadErrorStreamAsync(
        StreamReader reader,
        int generation,
        CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        try
        {
            int charactersRead;
            while ((charactersRead = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                ProcessErrorChunk(generation, new string(buffer, 0, charactersRead));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException ex) when (!IsDisposing)
        {
            Trace.TraceWarning("Command runner error stream failed: {0}", ex.Message);
        }
        catch (InvalidOperationException ex) when (!IsDisposing)
        {
            Trace.TraceWarning("Command runner error stream failed: {0}", ex.Message);
        }
        catch (IOException ex) when (!IsDisposing)
        {
            Trace.TraceWarning("Command runner error stream failed: {0}", ex.Message);
        }
        finally
        {
            CompleteError(generation);
        }
    }

    private void ProcessOutputChunk(int generation, string text)
    {
        lock (_stateLock)
        {
            if (generation != _shellGeneration || _outputFramer == null)
                return;

            ProcessFrames(_outputFramer.Append(text));
        }
    }

    private void CompleteOutput(int generation)
    {
        lock (_stateLock)
        {
            if (generation != _shellGeneration || _outputFramer == null)
                return;

            ProcessFrames(_outputFramer.Complete());
        }
    }

    private void ProcessErrorChunk(int generation, string text)
    {
        lock (_stateLock)
        {
            if (generation != _shellGeneration || _errorFramer == null)
                return;

            ProcessFrames(_errorFramer.Append(text));
        }
    }

    private void CompleteError(int generation)
    {
        lock (_stateLock)
        {
            if (generation != _shellGeneration || _errorFramer == null)
                return;

            ProcessFrames(_errorFramer.Complete());
        }
    }

    private void ProcessFrames(IReadOnlyList<TerminalOutputFrame> frames)
    {
        foreach (var frame in frames)
        {
            if (frame.Kind == TerminalOutputFrameKind.Output)
            {
                if (!string.IsNullOrEmpty(frame.Text))
                    QueueOutput(frame.Text);
                continue;
            }

            if (!frame.IsValidSentinel ||
                frame.CommandId != _expectedCommandId ||
                frame.CommandToken != _expectedCommandToken)
                continue;

            _expectedCommandId = 0;
            _expectedCommandToken = Guid.Empty;
            if (!string.IsNullOrEmpty(frame.WorkingDirectory) &&
                Directory.Exists(frame.WorkingDirectory) &&
                !string.Equals(_workingDirectory, frame.WorkingDirectory, StringComparison.Ordinal))
            {
                _workingDirectory = frame.WorkingDirectory;
                var directory = frame.WorkingDirectory;
                QueueNotification(() => WorkingDirectoryChanged?.Invoke(directory));
            }

            _isReady = true;
            _shellReadyCompletion?.TrySetResult();
            _shellReadyCompletion = null;
            QueueNotification(() => CommandCompleted?.Invoke());
        }
    }

    private async Task StopShellCoreAsync(
        string action,
        CancellationToken cancellationToken,
        bool force)
    {
        Process? process;
        CancellationTokenSource? readerCancellation;
        Task? outputReaderTask;
        Task? errorReaderTask;
        lock (_stateLock)
        {
            process = _shellProcess;
            readerCancellation = _readerCancellation;
            outputReaderTask = _outputReaderTask;
            errorReaderTask = _errorReaderTask;
        }

        if (process == null)
            return;

        try
        {
            if (!HasExited(process))
            {
                if (force)
                {
                    KillShellProcess(process, action);
                    if (!await WaitForExitAsync(process, ForcedExitTimeout, CancellationToken.None)
                            .ConfigureAwait(false))
                    {
                        Trace.TraceWarning(
                            "Command runner shell did not exit within {0} ms after kill while trying to {1}.",
                            ForcedExitTimeout.TotalMilliseconds,
                            action);
                    }
                }
                else
                {
                    TryRequestGracefulExit(process, action);
                    if (!await WaitForExitAsync(process, GracefulExitTimeout, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        Trace.TraceWarning(
                            "Command runner shell did not exit within {0} ms while trying to {1}; killing it.",
                            GracefulExitTimeout.TotalMilliseconds,
                            action);
                        KillShellProcess(process, action);
                        if (!await WaitForExitAsync(process, ForcedExitTimeout, CancellationToken.None)
                                .ConfigureAwait(false))
                        {
                            Trace.TraceWarning(
                                "Command runner shell did not exit within {0} ms after kill while trying to {1}.",
                                ForcedExitTimeout.TotalMilliseconds,
                                action);
                        }
                    }
                }
            }

            await WaitForReaderTasksAsync(
                    outputReaderTask,
                    errorReaderTask,
                    readerCancellation,
                    action)
                .ConfigureAwait(false);
        }
        finally
        {
            lock (_stateLock)
            {
                if (ReferenceEquals(_shellProcess, process))
                {
                    _shellProcess = null;
                    _readerCancellation = null;
                    _outputReaderTask = null;
                    _errorReaderTask = null;
                    _outputFramer = null;
                    _errorFramer = null;
                    _shellReadyCompletion?.TrySetCanceled();
                    _shellReadyCompletion = null;
                    _shellGeneration++;
                }
            }

            readerCancellation?.Dispose();
            process.Dispose();
        }
    }

    private static async Task<bool> WaitForExitAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (HasExited(process))
            return true;

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return HasExited(process);
        }
    }

    private static async Task WaitForReaderTasksAsync(
        Task? outputReaderTask,
        Task? errorReaderTask,
        CancellationTokenSource? readerCancellation,
        string action)
    {
        var readers = new[] { outputReaderTask, errorReaderTask }
            .Where(task => task != null)
            .Cast<Task>()
            .ToArray();
        if (readers.Length == 0)
            return;

        var combinedReaders = Task.WhenAll(readers);
        try
        {
            await combinedReaders.WaitAsync(ReaderExitTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Trace.TraceWarning(
                "Command runner stream readers did not finish within {0} ms while trying to {1}; cancelling reads.",
                ReaderExitTimeout.TotalMilliseconds,
                action);
            readerCancellation?.Cancel();
            try
            {
                await combinedReaders.WaitAsync(ReaderExitTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Trace.TraceWarning(
                    "Command runner stream readers remained active after cancellation while trying to {0}.",
                    action);
            }
        }
    }

    private async Task ShutdownCoreAsync()
    {
        lock (_stateLock)
        {
            if (_disposeStarted)
                return;
            _disposeStarted = true;
            _isReady = true;
        }

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopShellCoreAsync(
                    "dispose command runner",
                    CancellationToken.None,
                    force: false)
                .ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }

        lock (_notificationLock)
            _notificationsComplete = true;
        _notificationSignal.Release();
        try
        {
            await _notificationTask.WaitAsync(DefaultShutdownTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Trace.TraceWarning(
                "Command runner notification delivery did not finish within {0} ms during disposal.",
                DefaultShutdownTimeout.TotalMilliseconds);
        }
    }

    private async Task DeliverNotificationsAsync()
    {
        while (true)
        {
            await _notificationSignal.WaitAsync().ConfigureAwait(false);

            RunnerNotification? notification;
            lock (_notificationLock)
            {
                notification = _notifications.First?.Value;
                if (notification == null && _notificationsComplete)
                    return;
            }

            if (notification == null)
                continue;

            if (notification.Output != null)
                await Task.Delay(20).ConfigureAwait(false);

            lock (_notificationLock)
            {
                if (_notifications.First?.Value == notification)
                    _notifications.RemoveFirst();
            }

            if (notification.Output != null)
                OutputReceived?.Invoke(notification.Output.ToString());
            else
                notification.Callback?.Invoke();
        }
    }

    private void QueueOutput(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        lock (_notificationLock)
        {
            if (_notificationsComplete)
                return;

            if (_notifications.Last?.Value.Output is { } pendingOutput)
            {
                pendingOutput.Append(text);
                return;
            }

            _notifications.AddLast(RunnerNotification.ForOutput(text));
            _notificationSignal.Release();
        }
    }

    private void QueueNotification(Action notification)
    {
        lock (_notificationLock)
        {
            if (_notificationsComplete)
                return;

            _notifications.AddLast(RunnerNotification.ForCallback(notification));
            _notificationSignal.Release();
        }
    }

    private void SendRawCommand(Process process, string text)
    {
        if (HasExited(process))
            return;

        try
        {
            process.StandardInput.WriteLine(text);
            process.StandardInput.Flush();
        }
        catch (ObjectDisposedException ex) when (!IsDisposing)
        {
            Trace.TraceWarning("Failed to send command to shell: {0}", ex.Message);
        }
        catch (InvalidOperationException ex) when (!IsDisposing)
        {
            Trace.TraceWarning("Failed to send command to shell: {0}", ex.Message);
        }
        catch (IOException ex) when (!IsDisposing)
        {
            Trace.TraceWarning("Failed to send command to shell: {0}", ex.Message);
        }
    }

    private static void TryRequestGracefulExit(Process process, string action)
    {
        try
        {
            process.StandardInput.WriteLine("exit");
            process.StandardInput.Flush();
            process.StandardInput.Close();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException ex)
        {
            Trace.TraceWarning(
                "Failed to request graceful shell exit while trying to {0}: {1}",
                action,
                ex.Message);
        }
        catch (IOException ex)
        {
            Trace.TraceWarning(
                "Failed to request graceful shell exit while trying to {0}: {1}",
                action,
                ex.Message);
        }
    }

    private static void KillShellProcess(Process process, string action)
    {
        try
        {
            if (!HasExited(process))
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException ex)
        {
            Trace.TraceWarning("Failed to kill shell while trying to {0}: {1}", action, ex.Message);
        }
        catch (Win32Exception ex)
        {
            Trace.TraceWarning("Failed to kill shell while trying to {0}: {1}", action, ex.Message);
        }
        catch (NotSupportedException ex)
        {
            Trace.TraceWarning("Failed to kill shell while trying to {0}: {1}", action, ex.Message);
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private bool IsDisposing
    {
        get
        {
            lock (_stateLock)
                return _disposeStarted;
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposeStarted, this);
        }
    }

    private static string FindPowerShell()
    {
        var pwshPaths = new[]
        {
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            @"C:\Program Files (x86)\PowerShell\7\pwsh.exe"
        };

        foreach (var path in pwshPaths)
        {
            if (File.Exists(path))
                return path;
        }

        try
        {
            var pathDirectories =
                Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? Array.Empty<string>();
            foreach (var directory in pathDirectories)
            {
                var pwsh = Path.Combine(directory, "pwsh.exe");
                if (File.Exists(pwsh))
                    return pwsh;
            }
        }
        catch (ArgumentException ex)
        {
            Trace.TraceWarning("Failed to inspect PATH for PowerShell: {0}", ex.Message);
        }

        return "powershell.exe";
    }

    private static string EscapePath(string path) => path.Replace("'", "''");

    private sealed class RunnerNotification
    {
        private RunnerNotification(StringBuilder? output, Action? callback)
        {
            Output = output;
            Callback = callback;
        }

        public StringBuilder? Output { get; }
        public Action? Callback { get; }

        public static RunnerNotification ForOutput(string text) =>
            new(new StringBuilder(text), null);

        public static RunnerNotification ForCallback(Action callback) =>
            new(null, callback);
    }
}
