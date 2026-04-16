using System.Diagnostics;
using System.IO;
using FastEdit.Models;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public class GitService : IGitService
{
    public string? FindRepoRoot(string path)
    {
        try
        {
            var dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git")))
                    return dir;

                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
        }
        catch
        {
        }

        return null;
    }

    public bool IsGitRepository(string path)
    {
        return FindRepoRoot(path) != null;
    }

    public string GetCurrentBranch(string repoRoot)
    {
        try
        {
            var output = RunGit(repoRoot, "rev-parse --abbrev-ref HEAD");
            return output?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    public Dictionary<string, GitFileStatus> GetFileStatuses(string repoRoot)
    {
        var result = new Dictionary<string, GitFileStatus>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var output = RunGit(repoRoot, "status --porcelain=v1");
            if (string.IsNullOrEmpty(output))
                return result;

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length < 3) continue;

                var indexStatus = line[0];
                var workTreeStatus = line[1];
                var filePath = line.Substring(3).Trim();

                // Handle renamed files: "R  old -> new"
                var arrowIndex = filePath.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrowIndex >= 0)
                    filePath = filePath.Substring(arrowIndex + 4);

                var status = ParseStatus(indexStatus, workTreeStatus);
                result[filePath] = status;
            }
        }
        catch
        {
        }

        return result;
    }

    public async Task<Dictionary<string, GitFileStatus>> GetFileStatusesAsync(string repoRoot)
    {
        return await Task.Run(() => GetFileStatuses(repoRoot));
    }

    private static GitFileStatus ParseStatus(char indexStatus, char workTreeStatus)
    {
        if (indexStatus == '?' && workTreeStatus == '?')
            return GitFileStatus.Untracked;
        if (indexStatus == '!' && workTreeStatus == '!')
            return GitFileStatus.Ignored;
        if (indexStatus == 'R' || workTreeStatus == 'R')
            return GitFileStatus.Renamed;
        if (indexStatus == 'D' || workTreeStatus == 'D')
            return GitFileStatus.Deleted;
        if (indexStatus == 'A' || workTreeStatus == 'A')
            return GitFileStatus.Added;
        if (indexStatus == 'M' || workTreeStatus == 'M')
            return GitFileStatus.Modified;

        return GitFileStatus.Unmodified;
    }

    private static string? RunGit(string workingDirectory, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
