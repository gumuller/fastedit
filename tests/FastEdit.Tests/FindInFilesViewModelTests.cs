using System.IO;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;
using Moq;

namespace FastEdit.Tests;

public class FindInFilesViewModelTests
{
    private const string Root = @"C:\repo";
    private readonly Mock<IFileSystemService> _fileSystemService = new();
    private readonly Mock<IDispatcherService> _dispatcherService = new();

    public FindInFilesViewModelTests()
    {
        _fileSystemService.Setup(x => x.DirectoryExists(Root)).Returns(true);
        _dispatcherService
            .Setup(x => x.Invoke(It.IsAny<Action>()))
            .Callback<Action>(action => action());
    }

    [Fact]
    public async Task SearchCommand_LiteralSearch_AddsCaseInsensitiveMatches()
    {
        var file = Path.Combine(Root, "src", "log.txt");
        SetupFiles("*.txt", file);
        SetupTextFile(file, "ignore", "  Error: failed", "ERROR: again");

        var sut = CreateSut("error", "*.txt");

        await sut.SearchCommand.ExecuteAsync(null);

        Assert.Equal(2, sut.Results.Count);
        Assert.Equal("src\\log.txt", sut.Results[0].RelativePath);
        Assert.Equal(2, sut.Results[0].LineNumber);
        Assert.Equal("Error: failed", sut.Results[0].LineText);
        Assert.Equal("Found 2 match(es) in 1 file(s)", sut.StatusText);
        Assert.False(sut.IsSearching);
    }

    [Fact]
    public async Task SearchCommand_RegexSearch_UsesRegexMatcher()
    {
        var file = Path.Combine(Root, "sample.cs");
        SetupFiles("*.cs", file);
        SetupTextFile(file, "public class One", "private class Two");

        var sut = CreateSut(@"public\s+class", "*.cs");
        sut.UseRegex = true;

        await sut.SearchCommand.ExecuteAsync(null);

        Assert.Single(sut.Results);
        Assert.Equal("public class One", sut.Results[0].LineText);
    }

    [Fact]
    public async Task SearchCommand_FileFilter_SplitsAndDeduplicatesPatterns()
    {
        var csFile = Path.Combine(Root, "a.cs");
        var txtFile = Path.Combine(Root, "a.txt");
        SetupFiles("*.cs", csFile);
        SetupFiles("*.txt", txtFile, csFile);
        SetupTextFile(csFile, "needle");
        SetupTextFile(txtFile, "needle");

        var sut = CreateSut("needle", "*.cs;*.txt");

        await sut.SearchCommand.ExecuteAsync(null);

        Assert.Equal(2, sut.Results.Count);
        Assert.Equal("Found 2 match(es) in 2 file(s)", sut.StatusText);
    }

    [Fact]
    public async Task SearchCommand_BinaryAndInaccessibleFiles_AreSkipped()
    {
        var binaryFile = Path.Combine(Root, "binary.bin");
        var lockedFile = Path.Combine(Root, "locked.txt");
        var textFile = Path.Combine(Root, "ok.txt");
        SetupFiles("*.*", binaryFile, lockedFile, textFile);
        SetupBinaryFile(binaryFile);
        _fileSystemService.Setup(x => x.OpenRead(lockedFile)).Throws<UnauthorizedAccessException>();
        SetupTextFile(textFile, "needle");

        var sut = CreateSut("needle", "*.*");

        await sut.SearchCommand.ExecuteAsync(null);

        Assert.Single(sut.Results);
        Assert.Equal(textFile, sut.Results[0].FilePath);
        Assert.Equal("Found 1 match(es) in 3 file(s)", sut.StatusText);
    }

    [Fact]
    public async Task SearchCommand_InvalidRegex_ReportsErrorAndResetsSearchingState()
    {
        var file = Path.Combine(Root, "sample.txt");
        SetupFiles("*.txt", file);
        SetupTextFile(file, "anything");

        var sut = CreateSut("[", "*.txt");
        sut.UseRegex = true;

        await sut.SearchCommand.ExecuteAsync(null);

        Assert.StartsWith("Error:", sut.StatusText);
        Assert.False(sut.IsSearching);
    }

    [Fact]
    public async Task SearchCommand_Overlapping_Search_Ignores_Stale_Results()
    {
        var file = Path.Combine(Root, "sample.txt");
        SetupFiles("*.txt", file);
        SetupReadableFile(file);
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _fileSystemService
            .SetupSequence(x => x.ReadLines(file))
            .Returns(SlowLines(firstStarted, "first"))
            .Returns(["second"]);

        var sut = CreateSut("first", "*.txt");
        var firstSearch = sut.SearchCommand.ExecuteAsync(null);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        sut.SearchPattern = "second";
        var secondSearch = sut.SearchCommand.ExecuteAsync(null);
        await Task.WhenAll(firstSearch, secondSearch);

        var result = Assert.Single(sut.Results);
        Assert.Equal("second", result.LineText);
        Assert.Equal("Found 1 match(es) in 1 file(s)", sut.StatusText);
        Assert.False(sut.IsSearching);
    }

    [Fact]
    public async Task CancelSearchCommand_Reports_Cancellation_Without_Error()
    {
        var file = Path.Combine(Root, "sample.txt");
        SetupFiles("*.txt", file);
        SetupReadableFile(file);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _fileSystemService
            .Setup(x => x.ReadLines(file))
            .Returns(SlowLines(started, "needle"));

        var sut = CreateSut("needle", "*.txt");
        var search = sut.SearchCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        sut.CancelSearchCommand.Execute(null);
        await search;

        Assert.Equal("Search cancelled", sut.StatusText);
        Assert.False(sut.IsSearching);
    }

    [Fact]
    public async Task SearchCommand_ResultCap_Is_Exact_And_Uses_Batched_Dispatch()
    {
        var file = Path.Combine(Root, "sample.txt");
        SetupFiles("*.txt", file);
        SetupTextFile(file, Enumerable.Repeat("needle", FindInFilesViewModel.ResultLimit + 1).ToArray());
        var sut = CreateSut("needle", "*.txt");

        await sut.SearchCommand.ExecuteAsync(null);

        Assert.Equal(FindInFilesViewModel.ResultLimit, sut.Results.Count);
        Assert.Contains("result limit reached", sut.StatusText);
        _dispatcherService.Verify(
            x => x.Invoke(It.IsAny<Action>()),
            Times.AtMost(FindInFilesViewModel.ResultLimit / 50));
    }

    private FindInFilesViewModel CreateSut(string pattern, string filter) => new(_fileSystemService.Object, _dispatcherService.Object)
    {
        FolderPath = Root,
        SearchPattern = pattern,
        FileFilter = filter
    };

    private void SetupFiles(string filter, params string[] files)
    {
        _fileSystemService
            .Setup(x => x.EnumerateFiles(Root, filter, true))
            .Returns(files);
    }

    private void SetupTextFile(string file, params string[] lines)
    {
        SetupReadableFile(file);
        _fileSystemService
            .Setup(x => x.ReadLines(file))
            .Returns(lines);
    }

    private void SetupReadableFile(string file)
    {
        _fileSystemService
            .Setup(x => x.OpenRead(file))
            .Returns(() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes("text")));
    }

    private static IEnumerable<string> SlowLines(
        TaskCompletionSource started,
        string value)
    {
        started.TrySetResult();
        for (var i = 0; i < 10_000; i++)
        {
            Thread.Sleep(1);
            yield return value;
        }
    }

    private void SetupBinaryFile(string file)
    {
        _fileSystemService
            .Setup(x => x.OpenRead(file))
            .Returns(() => new MemoryStream([0x48, 0x00, 0x49]));
    }
}
