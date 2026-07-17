using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class ExternalChangeGenerationTrackerTests
{
    [Fact]
    public void TryApply_Rejects_Content_Read_Before_Newer_Change()
    {
        var tracker = new ExternalChangeGenerationTracker();
        tracker.RecordChange();
        var readGeneration = tracker.Capture();
        tracker.RecordChange();
        var applied = false;

        var generationWasCurrent = tracker.TryApply(readGeneration, () => applied = true);

        Assert.False(generationWasCurrent);
        Assert.False(applied);
    }

    [Fact]
    public void TryApply_Accepts_Content_From_Current_Change()
    {
        var tracker = new ExternalChangeGenerationTracker();
        tracker.RecordChange();
        var readGeneration = tracker.Capture();
        var applied = false;

        var generationWasCurrent = tracker.TryApply(readGeneration, () => applied = true);

        Assert.True(generationWasCurrent);
        Assert.True(applied);
    }
}
