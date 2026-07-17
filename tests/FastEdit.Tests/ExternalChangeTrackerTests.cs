using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class ExternalChangeTrackerTests
{
    [Fact]
    public void TryApply_Rejects_Read_When_Newer_Change_Was_Recorded()
    {
        var tracker = new ExternalChangeTracker();
        tracker.Record("file.txt");
        var stale = tracker.Capture("file.txt");
        tracker.Record("file.txt");
        var applied = false;

        var accepted = tracker.TryApply(stale.Generation, () => applied = true);

        Assert.False(accepted);
        Assert.False(applied);
    }

    [Fact]
    public void Capture_Uses_Latest_Pending_Path_And_Clears_It()
    {
        var tracker = new ExternalChangeTracker();
        tracker.Record("old.txt");
        tracker.Record("new.txt");

        var latest = tracker.Capture("fallback.txt");
        var next = tracker.Capture("fallback.txt");

        Assert.Equal("new.txt", latest.FilePath);
        Assert.Equal("fallback.txt", next.FilePath);
        Assert.True(next.Generation >= latest.Generation);
    }

    [Fact]
    public void Invalidate_Prevents_Previous_Generation_From_Applying()
    {
        var tracker = new ExternalChangeTracker();
        tracker.Record("file.txt");
        var snapshot = tracker.Capture("file.txt");
        tracker.Invalidate();

        Assert.False(tracker.TryApply(snapshot.Generation, () => { }));
        Assert.Null(tracker.TakePendingPath());
    }
}
