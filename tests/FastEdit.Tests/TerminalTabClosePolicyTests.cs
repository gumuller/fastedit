using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class TerminalTabClosePolicyTests
{
    [Fact]
    public void TryGetNextActiveIndex_ActiveMiddleTabClosed_SelectsSameIndex()
    {
        var hasNext = TerminalTabClosePolicy.TryGetNextActiveIndex(
            tabCountBeforeClose: 3,
            closedIndex: 1,
            closedTabWasActive: true,
            out var nextIndex);

        Assert.True(hasNext);
        Assert.Equal(1, nextIndex);
    }

    [Fact]
    public void TryGetNextActiveIndex_ActiveLastTabClosed_SelectsPreviousIndex()
    {
        var hasNext = TerminalTabClosePolicy.TryGetNextActiveIndex(
            tabCountBeforeClose: 3,
            closedIndex: 2,
            closedTabWasActive: true,
            out var nextIndex);

        Assert.True(hasNext);
        Assert.Equal(1, nextIndex);
    }

    [Theory]
    [InlineData(false, 3, 1)]
    [InlineData(true, 1, 0)]
    [InlineData(true, 3, -1)]
    public void TryGetNextActiveIndex_NoSelectionChange_ReturnsFalse(
        bool closedTabWasActive,
        int tabCountBeforeClose,
        int closedIndex)
    {
        var hasNext = TerminalTabClosePolicy.TryGetNextActiveIndex(
            tabCountBeforeClose,
            closedIndex,
            closedTabWasActive,
            out var nextIndex);

        Assert.False(hasNext);
        Assert.Equal(-1, nextIndex);
    }
}
