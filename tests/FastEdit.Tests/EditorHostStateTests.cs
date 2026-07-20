using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FastEdit.Services.Interfaces;
using FastEdit.Theming;
using FastEdit.ViewModels;
using FastEdit.Views;
using FastEdit.Views.Controls;
using Moq;

namespace FastEdit.Tests;

public class EditorHostStateTests
{
    [Fact]
    public async Task SwitchingTabs_CapturesInactiveStateForShutdownAndRestoresIt()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var first = CreateTab("first.txt");
            var second = CreateTab("second.txt");
            var sideBySide = CreateTab("side-by-side.txt");
            second.CursorOffset = 77;
            second.ScrollOffset = 9;
            var fileService = new Mock<IFileService>();
            var fileSystemService = new Mock<IFileSystemService>();
            var dialogService = new Mock<IDialogService>();
            var settingsService = new Mock<ISettingsService>();
            var themeService = new Mock<IThemeService>();
            settingsService.Setup(service => service.RecentFiles)
                .Returns(new List<string>());
            settingsService.Setup(service => service.EditorFontSize).Returns(14);
            themeService.Setup(service => service.AvailableThemes)
                .Returns(new List<ThemeDefinition>());
            var fileTree = new FileTreeViewModel(
                fileService.Object,
                settingsService.Object,
                dialogService.Object,
                fileSystemService.Object);
            var mainViewModel = new MainViewModel(
                fileService.Object,
                themeService.Object,
                settingsService.Object,
                dialogService.Object,
                fileSystemService.Object,
                Mock.Of<IEditorTabFactory>(),
                Mock.Of<IWorkspaceService>(),
                fileTree);
            var host = new EditorHost
            {
                Width = 400,
                Height = 200,
                LineFilterService = Mock.Of<ILineFilterService>(),
                ThemeService = themeService.Object,
                SettingsService = settingsService.Object,
                TextToolsService = Mock.Of<ITextToolsService>(),
                MacroService = Mock.Of<IMacroService>(),
                DialogService = dialogService.Object,
                FileSystemService = fileSystemService.Object,
                MainViewModel = mainViewModel,
                DataContext = first
            };
            var sideBySideHost = new EditorHost
            {
                Width = 400,
                Height = 200,
                LineFilterService = Mock.Of<ILineFilterService>(),
                ThemeService = themeService.Object,
                SettingsService = settingsService.Object,
                TextToolsService = Mock.Of<ITextToolsService>(),
                MacroService = Mock.Of<IMacroService>(),
                DialogService = dialogService.Object,
                FileSystemService = fileSystemService.Object,
                MainViewModel = mainViewModel,
                DataContext = sideBySide
            };
            var editors = new Grid();
            editors.ColumnDefinitions.Add(new ColumnDefinition());
            editors.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(sideBySideHost, 1);
            editors.Children.Add(host);
            editors.Children.Add(sideBySideHost);
            var window = new Window
            {
                Width = 400,
                Height = 200,
                Content = editors,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            window.Show();
            host.UpdateLayout();
            await host.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);
            sideBySideHost.TextEditor.CaretOffset = 88;
            MainWindow.CaptureEditorStates(editors);
            Assert.Equal(sideBySideHost.TextEditor.CaretOffset, sideBySide.CursorOffset);

            host.TextEditor.CaretOffset = 125;
            host.TextEditor.ScrollToLine(300);
            await host.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.Render);
            var expectedCursor = host.TextEditor.CaretOffset;
            var expectedScroll = host.TextEditor.VerticalOffset;
            first.Mode = FileOpenMode.Binary;
            Assert.Equal(expectedCursor, first.CursorOffset);
            Assert.Equal(expectedScroll, first.ScrollOffset);
            host.TextEditor.CaretOffset = 0;
            host.TextEditor.ScrollToHome();
            first.Mode = FileOpenMode.Text;
            await host.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);
            Assert.Equal(expectedCursor, host.TextEditor.CaretOffset);
            Assert.Equal(expectedScroll, host.TextEditor.VerticalOffset);
            host.DataContext = second;

            Assert.Equal(expectedCursor, first.CursorOffset);
            Assert.True(first.ScrollOffset > 0);

            var shutdownCursor = first.CursorOffset;
            var shutdownScroll = first.ScrollOffset;
            var restored = CreateTab("first.txt");
            restored.CursorOffset = shutdownCursor;
            restored.ScrollOffset = shutdownScroll;
            host.DataContext = first;
            Assert.Equal(77, second.CursorOffset);
            Assert.Equal(9, second.ScrollOffset);
            host.DataContext = restored;
            await host.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);

            Assert.Equal(shutdownCursor, host.TextEditor.CaretOffset);
            Assert.Equal(shutdownScroll, host.TextEditor.VerticalOffset);

            var binaryState = CreateBinaryTab();
            host.DataContext = binaryState;
            await host.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);
            binaryState.HexOffset = -1;
            host.DataContext = restored;
            Assert.Equal(2, binaryState.HexOffset);
            await host.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);

            var zeroState = CreateTab("zero.txt");
            zeroState.CursorOffset = 0;
            zeroState.ScrollOffset = 0;
            host.DataContext = zeroState;
            await host.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);
            Assert.Equal(0, host.TextEditor.CaretOffset);
            Assert.Equal(0, host.TextEditor.VerticalOffset);
            host.DataContext = restored;
            await host.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);

            window.Content = null;
            await host.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);
            host.TextEditor.CaretOffset = 0;
            host.TextEditor.ScrollToHome();
            window.Content = editors;
            await host.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);

            Assert.Equal(shutdownCursor, host.TextEditor.CaretOffset);
            Assert.Equal(shutdownScroll, host.TextEditor.VerticalOffset);

            host.DataContext = null;
            window.Close();
            first.Dispose();
            second.Dispose();
            sideBySide.Dispose();
            restored.Dispose();
            zeroState.Dispose();
            binaryState.Dispose();
        });
    }

    private static EditorTabViewModel CreateTab(string fileName)
    {
        var tab = new EditorTabViewModel(
            Mock.Of<IFileService>(),
            Mock.Of<IFileSystemService>(),
            Mock.Of<IDialogService>());
        tab.RestoreTextSnapshot(
            string.Join(Environment.NewLine, Enumerable.Range(0, 500)),
            fileName,
            filePath: null,
            Encoding.UTF8.CodePage,
            hasBom: false,
            isModified: true);
        return tab;
    }

    private static EditorTabViewModel CreateBinaryTab()
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
        return tab;
    }
}
