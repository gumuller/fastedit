using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class DebouncedActionCoordinatorTests
{
    [Fact]
    public async Task RunAsync_Executes_Only_Latest_Superseding_Action()
    {
        using var coordinator = new DebouncedActionCoordinator(TimeSpan.FromMilliseconds(30));
        var executions = new List<int>();

        var first = coordinator.RunAsync(_ =>
        {
            executions.Add(1);
            return Task.CompletedTask;
        });
        var second = coordinator.RunAsync(_ =>
        {
            executions.Add(2);
            return Task.CompletedTask;
        });

        await Task.WhenAll(first, second);

        Assert.Equal([2], executions);
    }

    [Fact]
    public async Task Dispose_Cancels_Pending_Action()
    {
        var coordinator = new DebouncedActionCoordinator(TimeSpan.FromSeconds(1));
        var executed = false;

        var pending = coordinator.RunAsync(_ =>
        {
            executed = true;
            return Task.CompletedTask;
        });
        coordinator.Dispose();
        await pending;

        Assert.False(executed);
    }
}
