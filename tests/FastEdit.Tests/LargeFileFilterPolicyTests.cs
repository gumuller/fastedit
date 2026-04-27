using System.IO;
using System.Text;
using FastEdit.Core.LargeFile;
using FastEdit.Models;
using FastEdit.Views.Controls;

namespace FastEdit.Tests;

public class LargeFileFilterPolicyTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); }
            catch { }
        }
    }

    [Fact]
    public void GetActiveFilters_Ignores_Disabled_And_Empty_Filters()
    {
        var filters = new[]
        {
            new LineFilter { Pattern = "error", IsEnabled = true },
            new LineFilter { Pattern = "", IsEnabled = true },
            new LineFilter { Pattern = "warning", IsEnabled = false }
        };

        var active = LargeFileFilterPolicy.GetActiveFilters(filters);

        Assert.Single(active);
        Assert.Equal("error", active[0].Pattern);
    }

    [Fact]
    public void ShouldShowLine_Includes_Matching_Includers_And_Removes_Exclusions()
    {
        var filters = LargeFileFilterPolicy.GetActiveFilters(new[]
        {
            new LineFilter { Pattern = "error" },
            new LineFilter { Pattern = "ignore", IsExcluding = true }
        });

        Assert.True(LargeFileFilterPolicy.ShouldShowLine("error: keep this", filters));
        Assert.False(LargeFileFilterPolicy.ShouldShowLine("error: ignore this", filters));
        Assert.False(LargeFileFilterPolicy.ShouldShowLine("info only", filters));
    }

    [Fact]
    public void ShouldShowLine_With_Only_Excluders_Shows_NonExcluded_Lines()
    {
        var filters = LargeFileFilterPolicy.GetActiveFilters(new[]
        {
            new LineFilter { Pattern = "ignore", IsExcluding = true }
        });

        Assert.True(LargeFileFilterPolicy.ShouldShowLine("normal line", filters));
        Assert.False(LargeFileFilterPolicy.ShouldShowLine("ignore this line", filters));
    }

    [Fact]
    public void MatchesNavigationFilter_Ignores_Exclude_Filters()
    {
        var filters = LargeFileFilterPolicy.GetActiveFilters(new[]
        {
            new LineFilter { Pattern = "error", IsExcluding = true }
        });

        Assert.False(LargeFileFilterPolicy.HasNavigationFilter(filters));
        Assert.False(LargeFileFilterPolicy.MatchesNavigationFilter("error", filters));
    }

    [Fact]
    public void FirstVisibleLineFilter_Returns_First_Enabled_Including_Match()
    {
        var filters = new[]
        {
            new LineFilter { Pattern = "skip", IsExcluding = true, BackgroundColor = "#111111" },
            new LineFilter { Pattern = "error", IsEnabled = false, BackgroundColor = "#222222" },
            new LineFilter { Pattern = "error", BackgroundColor = "#333333" }
        };

        var match = LargeFileFilterPolicy.FirstVisibleLineFilter("error line", filters);

        Assert.NotNull(match);
        Assert.Equal("#333333", match.BackgroundColor);
        Assert.Null(LargeFileFilterPolicy.FirstVisibleLineFilter("error skip", filters));
    }

    [Fact]
    public void TryFindAdjacentMatch_Wraps_Forward_And_Backward()
    {
        var matches = new long[] { 10, 20, 30 };

        Assert.True(LargeFileFilterPolicy.TryFindAdjacentMatch(matches, 20, forward: true, out var next));
        Assert.Equal(30, next);

        Assert.True(LargeFileFilterPolicy.TryFindAdjacentMatch(matches, 30, forward: true, out next));
        Assert.Equal(10, next);

        Assert.True(LargeFileFilterPolicy.TryFindAdjacentMatch(matches, 10, forward: false, out var previous));
        Assert.Equal(30, previous);
    }

    [Fact]
    public async Task ShouldShowLine_Creates_Compact_Line_List_For_LargeFileDocument()
    {
        var path = CreateTextFile("info\nerror keep\nerror ignore\nwarning keep");
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);

        var filters = LargeFileFilterPolicy.GetActiveFilters(new[]
        {
            new LineFilter { Pattern = "keep" },
            new LineFilter { Pattern = "ignore", IsExcluding = true }
        });

        var lines = await doc.FindMatchingLinesAsync(
            line => LargeFileFilterPolicy.ShouldShowLine(line, filters),
            maxResults: 100,
            onProgress: null,
            ct: CancellationToken.None);

        Assert.Equal(new long[] { 2, 4 }, lines);
    }

    private string CreateTextFile(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content, new UTF8Encoding(false));
        _tempFiles.Add(path);
        return path;
    }
}
