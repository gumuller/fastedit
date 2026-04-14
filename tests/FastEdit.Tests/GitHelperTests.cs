using FastEdit.Helpers;

namespace FastEdit.Tests;

public class GitHelperTests
{
    [Fact]
    public async Task GetBranchName_Returns_Null_For_Null_Path()
    {
        var result = await GitHelper.GetBranchNameAsync(null);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetBranchName_Returns_Null_For_Empty_Path()
    {
        var result = await GitHelper.GetBranchNameAsync("");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetBranchName_Returns_Null_For_NonExistent_Directory()
    {
        var result = await GitHelper.GetBranchNameAsync(@"C:\NonExistent\Path\file.txt");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRepoRoot_Returns_Null_For_Null_Path()
    {
        var result = await GitHelper.GetRepoRootAsync(null);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRepoRoot_Returns_Null_For_Empty_Path()
    {
        var result = await GitHelper.GetRepoRootAsync("");
        Assert.Null(result);
    }

    [Fact]
    public async Task IsGitAvailable_Returns_Bool()
    {
        // This test just verifies it doesn't throw
        var result = await GitHelper.IsGitAvailableAsync();
        // Git should be available on dev machines
        Assert.True(result);
    }

    [Fact]
    public async Task GetBranchName_Returns_Branch_For_Git_Repo()
    {
        // Use the FastEdit repo itself for testing
        var repoFile = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "README.md");
        var fullPath = System.IO.Path.GetFullPath(repoFile);

        if (!System.IO.File.Exists(fullPath))
        {
            // Skip if not running from repo
            return;
        }

        var branch = await GitHelper.GetBranchNameAsync(fullPath);
        Assert.NotNull(branch);
        Assert.NotEmpty(branch);
    }
}
