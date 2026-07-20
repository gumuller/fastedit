using System.Windows;
using System.Windows.Threading;

namespace FastEdit.Tests;

internal static class WpfTestHost
{
    private static readonly Lazy<Dispatcher> DispatcherInstance =
        new(CreateDispatcher);

    public static Task RunAsync(Func<Task> action)
    {
        return DispatcherInstance.Value
            .InvokeAsync(action, DispatcherPriority.Normal)
            .Task
            .Unwrap()
            .WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static Dispatcher CreateDispatcher()
    {
        var ready = new TaskCompletionSource<Dispatcher>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var application = new Application();
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/FastEdit;component/Themes/ThemeResources.xaml")
            });
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }
}
