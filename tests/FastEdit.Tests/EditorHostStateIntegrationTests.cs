using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FastEdit.Infrastructure;
using FastEdit.Services;
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

    [Fact]
    public async Task ModeSpecificViewportStateRoundTripsAcrossSwitches()
    {
        await RunOnStaThreadAsync(async () =>
        {
            EnsureApplicationResources();
            var workspace = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            EditorTabViewModel? firstBinary = null;
            EditorTabViewModel? secondBinary = null;
            EditorTabViewModel? firstLarge = null;
            EditorTabViewModel? secondLarge = null;
            try
            {
                var fileSystem = new FileSystemService();
                var firstBinaryPath = Path.Combine(workspace, "first.bin");
                var secondBinaryPath = Path.Combine(workspace, "second.bin");
                File.WriteAllBytes(firstBinaryPath, Enumerable.Range(0, 4096)
                    .Select(value => (byte)value).ToArray());
                File.WriteAllBytes(secondBinaryPath, Enumerable.Range(0, 4096)
                    .Select(value => (byte)value).ToArray());
                firstBinary = CreateBinaryTab(
                    fileSystem,
                    firstBinaryPath,
                    hexOffset: 11,
                    scrollOffset: 2,
                    bytesPerRow: 8);
                secondBinary = CreateBinaryTab(
                    fileSystem,
                    secondBinaryPath,
                    hexOffset: 23,
                    scrollOffset: 3,
                    bytesPerRow: 16);
                var host = new EditorHost { Width = 500, Height = 300 };
                host.DataContext = firstBinary;
                host.Measure(new Size(500, 300));
                host.Arrange(new Rect(0, 0, 500, 300));
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var hexEditor = Assert.IsType<HexEditorControl>(
                    host.FindName("HexEditor"));
                hexEditor.ApplyStateFromViewModel(
                    firstBinary,
                    selectedOffset: 17,
                    scrollOffset: 4,
                    bytesPerRow: 8);

                host.DataContext = secondBinary;
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);

                Assert.Equal(17, firstBinary.HexOffset);
                Assert.Equal(4, firstBinary.ScrollOffset);
                hexEditor.CaptureStateToViewModel(secondBinary);
                Assert.Equal(23, secondBinary.HexOffset);
                Assert.Equal(3, secondBinary.ScrollOffset);
                Assert.Equal(16, secondBinary.BytesPerRow);

                var firstLargePath = Path.Combine(workspace, "first-large.txt");
                var secondLargePath = Path.Combine(workspace, "second-large.txt");
                await File.WriteAllTextAsync(
                    firstLargePath,
                    string.Join('\n', Enumerable.Range(1, 200)));
                await File.WriteAllTextAsync(
                    secondLargePath,
                    string.Join('\n', Enumerable.Range(1, 200)));
                firstLarge = CreateTab(string.Empty, 0, 0);
                await firstLarge.RestoreLargeTextViewAsync(
                    firstLargePath,
                    "first-large.txt",
                    firstLargePath);
                firstLarge.LargeFileTopLine = 3;
                secondLarge = CreateTab(string.Empty, 0, 0);
                await secondLarge.RestoreLargeTextViewAsync(
                    secondLargePath,
                    "second-large.txt",
                    secondLargePath);
                secondLarge.LargeFileTopLine = 5;
                var viewer = new LargeFileViewer
                {
                    Width = 500,
                    Height = 300,
                    DataContext = firstLarge
                };
                viewer.Measure(new Size(500, 300));
                viewer.Arrange(new Rect(0, 0, 500, 300));
                viewer.UpdateLayout();
                viewer.DataContext = secondLarge;
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                Assert.Equal(3, firstLarge.LargeFileTopLine);
                viewer.CaptureStateToViewModel();
                Assert.Equal(5, secondLarge.LargeFileTopLine);
                viewer.DataContext = firstLarge;
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                viewer.CaptureStateToViewModel();
                Assert.Equal(3, firstLarge.LargeFileTopLine);
                viewer.GoToLine(7);

                viewer.DataContext = secondLarge;
                viewer.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);

                Assert.Equal(7, firstLarge.LargeFileTopLine);
                viewer.CaptureStateToViewModel();
                Assert.Equal(5, secondLarge.LargeFileTopLine);
                viewer.DataContext = firstLarge;
                viewer.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                viewer.CaptureStateToViewModel();
                Assert.Equal(7, firstLarge.LargeFileTopLine);

            }
            finally
            {
                firstBinary?.Dispose();
                secondBinary?.Dispose();
                firstLarge?.Dispose();
                secondLarge?.Dispose();
                Directory.Delete(workspace, recursive: true);
            }
        });
    }

    [Fact]
    public async Task HexNoSelectionRoundTripsAcrossTabSwitchWithoutEnablingEdit()
    {
        await RunOnStaThreadAsync(async () =>
        {
            EnsureApplicationResources();
            var workspace = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            EditorTabViewModel? first = null;
            EditorTabViewModel? second = null;
            try
            {
                var fileSystem = new FileSystemService();
                var firstPath = Path.Combine(workspace, "first.bin");
                var secondPath = Path.Combine(workspace, "second.bin");
                File.WriteAllBytes(firstPath, new byte[] { 0x12, 0x34 });
                File.WriteAllBytes(secondPath, new byte[] { 0x56, 0x78 });
                first = CreateBinaryTab(
                    fileSystem,
                    firstPath,
                    hexOffset: -1,
                    scrollOffset: 0,
                    bytesPerRow: 8);
                second = CreateBinaryTab(
                    fileSystem,
                    secondPath,
                    hexOffset: long.MaxValue,
                    scrollOffset: 0,
                    bytesPerRow: 8);
                var host = new EditorHost { Width = 500, Height = 300 };
                host.DataContext = first;
                host.Measure(new Size(500, 300));
                host.Arrange(new Rect(0, 0, 500, 300));
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var hexEditor = Assert.IsType<HexEditorControl>(
                    host.FindName("HexEditor"));

                Assert.Equal(-1, hexEditor.SelectedOffset);
                Assert.False(hexEditor.CanEditSelection);

                host.DataContext = second;
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);

                Assert.Equal(-1, first.HexOffset);
                Assert.Equal(
                    second.ByteBuffer!.Length - 1,
                    hexEditor.SelectedOffset);

                host.DataContext = first;
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);

                Assert.Equal(-1, hexEditor.SelectedOffset);
                Assert.False(hexEditor.CanEditSelection);
                var decision = HexEditorKeyInputPolicy.Decide(
                    Key.D1,
                    ModifierKeys.None,
                    isSearchVisible: false,
                    isEditorInputSource: true,
                    hasSelection: hexEditor.CanEditSelection,
                    selectedOffset: hexEditor.SelectedOffset,
                    bufferLength: first.ByteBuffer!.Length,
                    bytesPerRow: first.BytesPerRow,
                    visibleRows: 1);
                Assert.Equal(HexEditorKeyAction.None, decision.Action);
                Assert.Equal(0x12, first.ByteBuffer.GetByte(0));
            }
            finally
            {
                first?.Dispose();
                second?.Dispose();
                Directory.Delete(workspace, recursive: true);
            }
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

    private static EditorTabViewModel CreateBinaryTab(
        IFileSystemService fileSystem,
        string backingPath,
        long hexOffset,
        double scrollOffset,
        int bytesPerRow)
    {
        var tab = new EditorTabViewModel(
            Mock.Of<IFileService>(),
            fileSystem,
            Mock.Of<IDialogService>())
        {
            ScrollOffset = scrollOffset
        };
        tab.RestoreBinarySnapshot(
            backingPath,
            Path.GetFileName(backingPath),
            backingPath,
            isModified: false,
            hexOffset,
            bytesPerRow);
        return tab;
    }

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
