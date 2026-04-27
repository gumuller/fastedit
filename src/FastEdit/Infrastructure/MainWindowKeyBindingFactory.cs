using System.Windows.Input;
using FastEdit.ViewModels;

namespace FastEdit.Infrastructure;

public static class MainWindowKeyBindingFactory
{
    public static IReadOnlyDictionary<string, ICommand> CreateCommandMap(MainViewModel viewModel) =>
        new Dictionary<string, ICommand>
        {
            ["NewFile"] = viewModel.NewFileCommand,
            ["OpenFile"] = viewModel.OpenFileCommand,
            ["Save"] = viewModel.SaveCommand,
            ["SaveAs"] = viewModel.SaveAsCommand,
            ["CloseTab"] = viewModel.CloseTabCommand,
            ["Find"] = viewModel.FindCommand,
            ["Replace"] = viewModel.ReplaceCommand,
            ["GoToLine"] = viewModel.GoToLineCommand,
            ["DuplicateLine"] = viewModel.DuplicateLineCommand,
            ["MoveLineUp"] = viewModel.MoveLineUpCommand,
            ["MoveLineDown"] = viewModel.MoveLineDownCommand,
            ["ZoomIn"] = viewModel.ZoomInCommand,
            ["ZoomOut"] = viewModel.ZoomOutCommand,
            ["ResetZoom"] = viewModel.ResetZoomCommand,
            ["FindInFiles"] = viewModel.FindInFilesCommand,
            ["ToggleBookmark"] = viewModel.ToggleBookmarkCommand,
            ["NextBookmark"] = viewModel.NextBookmarkCommand,
            ["PrevBookmark"] = viewModel.PrevBookmarkCommand,
            ["CommandPalette"] = viewModel.CommandPaletteCommand,
            ["Completion"] = viewModel.ShowCompletionCommand,
            ["ToggleTerminal"] = viewModel.ToggleCommandRunnerCommand,
            ["SplitView"] = viewModel.ToggleSplitViewCommand,
            ["Print"] = viewModel.PrintCommand,
            ["SelectNextOccurrence"] = viewModel.SelectNextOccurrenceCommand,
            ["SelectAllOccurrences"] = viewModel.SelectAllOccurrencesCommand,
            ["Settings"] = viewModel.OpenSettingsCommand,
            ["ZenMode"] = viewModel.ToggleZenModeCommand,
            ["ToggleExplorer"] = viewModel.ToggleExplorerCommand,
            ["ToggleFilterPanel"] = viewModel.ToggleFilterPanelCommand,
        };

    public static IReadOnlyList<KeyBinding> CreateKeyBindings(
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlyDictionary<string, ICommand> commandMap)
    {
        var keyBindings = new List<KeyBinding>();

        foreach (var (commandName, gestureText) in bindings)
        {
            if (!commandMap.TryGetValue(commandName, out var command)) continue;

            var gesture = KeyGestureParser.Parse(gestureText);
            if (gesture != null)
            {
                keyBindings.Add(new KeyBinding(command, gesture));
            }
        }

        return keyBindings;
    }
}
