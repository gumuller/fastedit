using System.Diagnostics;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public class ProcessService : IProcessService
{
    public IProcessHandle Start(ProcessStartOptions options)
    {
        var psi = new ProcessStartInfo
        {
            FileName = options.FileName,
            Arguments = options.Arguments,
            WorkingDirectory = options.WorkingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardInput = options.RedirectStandardInput,
            RedirectStandardOutput = options.RedirectStandardOutput,
            RedirectStandardError = options.RedirectStandardError,
            CreateNoWindow = options.CreateNoWindow,
            UseShellExecute = false
        };

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process");
        return new ProcessHandle(process);
    }

    public async Task<string?> RunToCompletionAsync(string fileName, string arguments, string? workingDirectory = null, int timeoutMs = 5000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            using var cts = new CancellationTokenSource(timeoutMs);
            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private class ProcessHandle : IProcessHandle
    {
        private readonly Process _process;

        public ProcessHandle(Process process)
        {
            _process = process;
            _process.EnableRaisingEvents = true;

            if (_process.StartInfo.RedirectStandardOutput)
            {
                _process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null) OutputReceived?.Invoke(e.Data);
                };
                _process.BeginOutputReadLine();
            }

            if (_process.StartInfo.RedirectStandardError)
            {
                _process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null) ErrorReceived?.Invoke(e.Data);
                };
                _process.BeginErrorReadLine();
            }

            _process.Exited += (_, _) => Exited?.Invoke();
        }

        public int Id => _process.Id;
        public bool HasExited => _process.HasExited;
        public event Action<string>? OutputReceived;
        public event Action<string>? ErrorReceived;
        public event Action? Exited;

        public void WriteInput(string text)
        {
            _process.StandardInput.WriteLine(text);
            _process.StandardInput.Flush();
        }

        public void Kill()
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch { }
        }

        public void Dispose()
        {
            Kill();
            _process.Dispose();
        }
    }
}
