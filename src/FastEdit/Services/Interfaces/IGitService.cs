using FastEdit.Models;

namespace FastEdit.Services.Interfaces;

public interface IGitService
{
    string? FindRepoRoot(string path);
    bool IsGitRepository(string path);
    string GetCurrentBranch(string repoRoot);
    Dictionary<string, GitFileStatus> GetFileStatuses(string repoRoot);
    Task<Dictionary<string, GitFileStatus>> GetFileStatusesAsync(string repoRoot);
}
