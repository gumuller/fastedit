using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;
using FastEdit.Views.Controls;
using ICSharpCode.AvalonEdit;
using Moq;

namespace FastEdit.Tests;

public class EditorHostStateIntegrationTests
{
    [Fact]
    public async Task ContentReplacementKeepsLiveScrollWhileTabSwitchRestoresPersistedState()
    {
        await RunOnStaThreadAsync(async () =>
        {
            EnsureApplicationResources();
            var host = new EditorHost
            {
                Width = 500,
                Height = 300
            };
            var first = CreateTab(CreateLines(400), cursorOffset: 7, scrollOffset: 30);
            host.DataContext = first;
            host.Measure(new Size(500, 300));
            host.Arrange(new Rect(0, 0, 500, 300));
            host.UpdateLayout();
            var editor = Assert.IsType<TextEditor>(host.FindName("TextEditor"));

            host.ApplyExternalReloadContent(first, first.Content);
            host.UpdateLayout();
            var tailOffset = editor.VerticalOffset;
            var liveCaretOffset = editor.CaretOffset;
            Assert.True(tailOffset > 0);

            await Dispatcher.Yield(DispatcherPriority.Loaded);
            Assert.Equal(tailOffset, editor.VerticalOffset, precision: 3);
            Assert.Equal(liveCaretOffset, editor.CaretOffset);

            var second = CreateTab(CreateLines(300), cursorOffset: 12, scrollOffset: 50);
            host.DataContext = second;
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);

            Assert.Equal(12, editor.CaretOffset);
            Assert.Equal(50, editor.VerticalOffset, precision: 3);
            Assert.Equal(liveCaretOffset, first.CursorOffset);
            Assert.Equal(tailOffset, first.ScrollOffset, precision: 3);

            host.DataContext = first;
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);

            Assert.Equal(liveCaretOffset, editor.CaretOffset);
            Assert.Equal(tailOffset, editor.VerticalOffset, precision: 3);
        });
    }

    private static EditorTabViewModel CreateTab(
        string content,
        int cursorOffset,
        double scrollOffset)
    {
        var tab = new EditorTabViewModel(
            Mock.Of<IFileService>(),
            Mock.Of<IFileSystemService>(),
            Mock.Of<IDialogService>())
        {
            CursorOffset = cursorOffset,
            ScrollOffset = scrollOffset
        };
        tab.SetContentBaseline(content, isModified: false);
        return tab;
    }

    private static string CreateLines(int count) =>
        string.Join(Environment.NewLine, Enumerable.Range(1, count));

    private static void EnsureApplicationResources()
    {
        var application = Application.Current ?? new Application();
        application.Resources["FastEditSearchTextBoxStyle"] =
            new Style(typeof(System.Windows.Controls.TextBox));
        application.Resources["FastEditSearchButtonStyle"] =
            new Style(typeof(System.Windows.Controls.Button));
    }

    private static Task RunOnStaThreadAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(dispatcher));
            _ = dispatcher.BeginInvoke(new Action(async () =>
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
