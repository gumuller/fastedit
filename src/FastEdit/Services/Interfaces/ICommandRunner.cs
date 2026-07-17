namespace FastEdit.Services.Interfaces;

public interface ICommandRunner : IDisposable, IAsyncDisposable
{
    event Action<string>? OutputReceived;
    event Action<string>? WorkingDirectoryChanged;
    event Action? CommandStarted;
    event Action? CommandCompleted;

    string WorkingDirectory { get; }
    IReadOnlyList<string> History { get; }
    bool IsRunning { get; }
    bool IsBusy { get; }

    Task StartShellAsync(string? initialDirectory = null, CancellationToken cancellationToken = default);
    Task ExecuteCommandAsync(string command, CancellationToken cancellationToken = default);
    Task StopCurrentProcessAsync(CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
    string? GetPreviousHistoryItem();
    string? GetNextHistoryItem();
    Task<bool> SetWorkingDirectoryAsync(
        string? directory,
        CancellationToken cancellationToken = default);
}
