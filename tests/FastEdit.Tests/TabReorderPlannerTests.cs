using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class TabReorderPlannerTests
{
    [Fact]
    public void TryCreateMove_ValidDifferentIndexes_ReturnsMove()
    {
        var created = TabReorderPlanner.TryCreateMove(2, 0, out var move);

        Assert.True(created);
        Assert.Equal(new TabReorderMove(2, 0), move);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(1, 1)]
    public void TryCreateMove_InvalidOrSameIndex_ReturnsFalse(int sourceIndex, int targetIndex)
    {
        var created = TabReorderPlanner.TryCreateMove(sourceIndex, targetIndex, out _);

        Assert.False(created);
    }
}
