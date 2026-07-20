using System.Windows;
using System.Windows.Threading;
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
        await WpfTestHost.RunAsync(() =>
        {
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

    [Fact]
    public async Task UnloadCapturesBinaryStateBeforeReloadRestoresIt()
    {
        await WpfTestHost.RunAsync(async () =>
        {
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
            var control = new HexEditorControl { DataContext = tab };
            var window = new Window
            {
                Content = control,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            window.Show();
            await control.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);
            tab.HexOffset = -1;

            window.Content = null;
            await control.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);

            Assert.Equal(2, tab.HexOffset);
            window.Content = control;
            await control.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);
            tab.HexOffset = -1;
            control.SaveStateToViewModel();
            Assert.Equal(2, tab.HexOffset);

            control.DataContext = null;
            window.Close();
            tab.Dispose();
        });
    }

}
