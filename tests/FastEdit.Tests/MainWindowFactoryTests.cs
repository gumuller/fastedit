using System.Windows.Input;
using FastEdit.Infrastructure;
using FastEdit.Services.Interfaces;
using FastEdit.Theming;
using FastEdit.ViewModels;
using Moq;

namespace FastEdit.Tests;

public class MainWindowFactoryTests
{
    [Fact]
    public void CreateCommandRegistry_RegistersMainWindowCommands()
    {
        var viewModel = CreateViewModel();

        var registry = MainWindowCommandRegistryFactory.Create(viewModel);

        Assert.Equal(65, registry.Commands.Count);
        Assert.Contains(registry.Commands, c =>
            c.Name == "Save" &&
            c.Category == "File" &&
            c.ShortcutText == "Ctrl+S" &&
            ReferenceEquals(c.Command, viewModel.SaveCommand));
        Assert.Contains(registry.Commands, c => c.Name == "Zoom In" && c.ShortcutText == "Ctrl++");
        Assert.Contains(registry.Commands, c => c.Name == "Checksum: SHA-512" && c.Category == "Text Tools");
    }

    [Fact]
    public void CreateCommandMap_MapsShortcutIdsToViewModelCommands()
    {
        var viewModel = CreateViewModel();

        var commandMap = MainWindowKeyBindingFactory.CreateCommandMap(viewModel);

        Assert.Equal(29, commandMap.Count);
        Assert.True(ReferenceEquals(viewModel.SaveCommand, commandMap["Save"]));
        Assert.True(ReferenceEquals(viewModel.ToggleCommandRunnerCommand, commandMap["ToggleTerminal"]));
        Assert.True(ReferenceEquals(viewModel.ToggleFilterPanelCommand, commandMap["ToggleFilterPanel"]));
    }

    [Fact]
    public void CreateKeyBindings_IgnoresUnknownCommandsAndInvalidGestures()
    {
        var command = new TestCommand();
        var commandMap = new Dictionary<string, ICommand>
        {
            ["Save"] = command
        };
        var bindings = new Dictionary<string, string>
        {
            ["Save"] = "Ctrl+S",
            ["Unknown"] = "Ctrl+U",
            ["SaveAs"] = "Meta+S"
        };

        var keyBindings = MainWindowKeyBindingFactory.CreateKeyBindings(bindings, commandMap);

        var keyBinding = Assert.Single(keyBindings);
        Assert.True(ReferenceEquals(command, keyBinding.Command));
        Assert.Equal(Key.S, keyBinding.Key);
        Assert.Equal(ModifierKeys.Control, keyBinding.Modifiers);
    }

    private static MainViewModel CreateViewModel()
    {
        var fileService = new Mock<IFileService>();
        var themeService = new Mock<IThemeService>();
        var settingsService = new Mock<ISettingsService>();
        var dialogService = new Mock<IDialogService>();
        var fileSystemService = new Mock<IFileSystemService>();
        var tabFactory = new Mock<IEditorTabFactory>();
        var workspaceService = new Mock<IWorkspaceService>();

        themeService.Setup(t => t.AvailableThemes).Returns(new List<ThemeDefinition>());
        themeService.Setup(t => t.CurrentTheme).Returns((ThemeDefinition)null!);
        settingsService.Setup(s => s.RecentFiles).Returns(new List<string>());
        settingsService.Setup(s => s.WordWrapEnabled).Returns(false);
        settingsService.Setup(s => s.ShowWhitespace).Returns(false);
        settingsService.Setup(s => s.EditorFontSize).Returns(14);
        fileSystemService.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(true);
        fileSystemService.Setup(f => f.GetDirectories(It.IsAny<string>())).Returns(Array.Empty<string>());
        fileSystemService.Setup(f => f.GetFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>())).Returns(Array.Empty<string>());

        var fileTree = new FileTreeViewModel(
            fileService.Object,
            settingsService.Object,
            dialogService.Object,
            fileSystemService.Object);

        return new MainViewModel(
            fileService.Object,
            themeService.Object,
            settingsService.Object,
            dialogService.Object,
            fileSystemService.Object,
            tabFactory.Object,
            workspaceService.Object,
            fileTree);
    }

    private sealed class TestCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }
}
