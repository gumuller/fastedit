namespace FastEdit.Services.Interfaces;

public interface IProcessService
{
    /// <summary>Start a process and return a handle for interaction.</summary>
    IProcessHandle Start(ProcessStartOptions options);

    /// <summary>Run a process to completion and return stdout. Returns null on failure.</summary>
    Task<string?> RunToCompletionAsync(string fileName, string arguments, string? workingDirectory = null, int timeoutMs = 5000);
}

public interface IProcessHandle : IDisposable
{
    int Id { get; }
    bool HasExited { get; }
    event Action<string>? OutputReceived;
    event Action<string>? ErrorReceived;
    event Action? Exited;
    void WriteInput(string text);
    void Kill();
}

public class ProcessStartOptions
{
    public string FileName { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string? WorkingDirectory { get; set; }
    public bool RedirectStandardInput { get; set; }
    public bool RedirectStandardOutput { get; set; }
    public bool RedirectStandardError { get; set; }
    public bool CreateNoWindow { get; set; } = true;
}
