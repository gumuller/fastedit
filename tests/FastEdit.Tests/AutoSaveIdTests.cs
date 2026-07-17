using FastEdit.Infrastructure;
using FluentAssertions;

namespace FastEdit.Tests;

public class AutoSaveIdTests
{
    [Fact]
    public void ForFilePath_ShortPath_ReturnsStableSha256Id()
    {
        var first = AutoSaveId.ForFilePath("a");
        var second = AutoSaveId.ForFilePath("a");

        first.Should().Be(second);
        first.Should().HaveLength(64);
    }

    [Fact]
    public void ForFilePath_CommonWindowsPrefix_DoesNotCollide()
    {
        var first = AutoSaveId.ForFilePath(@"C:\Users\person\source\alpha.txt");
        var second = AutoSaveId.ForFilePath(@"C:\Users\person\source\beta.txt");

        first.Should().NotBe(second);
    }

    [Fact]
    public void ForUntitled_StableKeysProduceStableDistinctIds()
    {
        AutoSaveId.ForUntitled("tab-1").Should().Be(AutoSaveId.ForUntitled("tab-1"));
        AutoSaveId.ForUntitled("tab-1").Should().NotBe(AutoSaveId.ForUntitled("tab-2"));
    }
}
