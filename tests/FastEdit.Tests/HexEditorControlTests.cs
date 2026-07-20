using System.Windows.Threading;
using System.Windows;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;
using FastEdit.Views.Controls;
using Moq;

namespace FastEdit.Tests;

public class HexEditorControlTests
{
    [Fact]
    public async Task RestoredSelectionAndScrollAreAppliedToControlState()
    {
        await RunOnStaThreadAsync(() =>
        {
            var application = new Application();
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/FastEdit;component/Themes/ThemeResources.xaml")
            });
            var tab = new EditorTabViewModel(
                Mock.Of<IFileService>(),
                Mock.Of<IFileSystemService>(),
                Mock.Of<IDialogService>());
            tab.RestoreBinarySnapshot(
                new byte[] { 1, 2, 3, 4 },
                "binary.bin",
                filePath: null,
                isModified: true);
            tab.HexOffset = 2;
            tab.HexScrollOffset = 0;
            var control = new HexEditorControl { DataContext = tab };

            control.RestoreStateFromViewModel();
            tab.HexOffset = -1;
            tab.HexScrollOffset = -1;
            control.SaveStateToViewModel();

            Assert.Equal(2, tab.HexOffset);
            Assert.Equal(0, tab.HexScrollOffset);
            control.DataContext = null;
            tab.Dispose();
            return Task.CompletedTask;
        });
    }

    private static Task RunOnStaThreadAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await action();
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
