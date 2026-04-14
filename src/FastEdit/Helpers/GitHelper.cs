using System.Diagnostics;
using System.IO;

namespace FastEdit.Helpers;

/// <summary>
/// Lightweight git integration using CLI commands.
/// </summary>
public static class GitHelper
{
    /// <summary>
    /// Gets the current branch name for a file path, or null if not in a git repo.
    /// </summary>
    public static async Task<string?> GetBranchNameAsync(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;

        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

        var result = await RunGitAsync("rev-parse --abbrev-ref HEAD", dir);
        return result?.Trim();
    }

    /// <summary>
    /// Gets the repository root path for a file.
    /// </summary>
    public static async Task<string?> GetRepoRootAsync(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;

        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir)) return null;

        var result = await RunGitAsync("rev-parse --show-toplevel", dir);
        return result?.Trim();
    }

    /// <summary>
    /// Checks if git is available on the system.
    /// </summary>
    public static async Task<bool> IsGitAvailableAsync()
    {
        var result = await RunGitAsync("--version", null);
        return result != null;
    }

    private static async Task<string?> RunGitAsync(string arguments, string? workingDirectory)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!string.IsNullOrEmpty(workingDirectory))
                psi.WorkingDirectory = workingDirectory;

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(); } catch { }
                return null;
            }

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
