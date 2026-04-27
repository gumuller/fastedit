using FastEdit.ViewModels;

namespace FastEdit.Infrastructure;

public static class MainWindowCommandRegistryFactory
{
    public static CommandRegistry Create(MainViewModel viewModel)
    {
        var registry = new CommandRegistry();

        registry.Register("New File", "File", "Ctrl+N", viewModel.NewFileCommand);
        registry.Register("Open File", "File", "Ctrl+O", viewModel.OpenFileCommand);
        registry.Register("Open Folder", "File", null, viewModel.OpenFolderCommand);
        registry.Register("Add Folder to Workspace", "File", null, viewModel.AddFolderCommand);
        registry.Register("Open Workspace", "File", null, viewModel.OpenWorkspaceCommand);
        registry.Register("Save Workspace As", "File", null, viewModel.SaveWorkspaceCommand);
        registry.Register("Save Session As", "File", null, viewModel.SaveSessionAsCommand);
        registry.Register("Save", "File", "Ctrl+S", viewModel.SaveCommand);
        registry.Register("Save As", "File", "Ctrl+Shift+S", viewModel.SaveAsCommand);
        registry.Register("Print", "File", "Ctrl+P", viewModel.PrintCommand);
        registry.Register("Select Next Occurrence", "Edit", "Ctrl+D", viewModel.SelectNextOccurrenceCommand);
        registry.Register("Select All Occurrences", "Edit", "Ctrl+Shift+L", viewModel.SelectAllOccurrencesCommand);
        registry.Register("Start Macro Recording", "Edit", null, viewModel.MacroStartRecordingCommand);
        registry.Register("Stop Macro Recording", "Edit", null, viewModel.MacroStopRecordingCommand);
        registry.Register("Playback Macro", "Edit", null, viewModel.MacroPlaybackCommand);
        registry.Register("Playback Macro Multiple", "Edit", null, viewModel.MacroPlaybackMultipleCommand);
        registry.Register("Close Tab", "File", "Ctrl+W", viewModel.CloseTabCommand);

        registry.Register("Find", "Edit", "Ctrl+F", viewModel.FindCommand);
        registry.Register("Replace", "Edit", "Ctrl+H", viewModel.ReplaceCommand);
        registry.Register("Find in Files", "Edit", "Ctrl+Shift+F", viewModel.FindInFilesCommand);
        registry.Register("Go to Line", "Edit", "Ctrl+G", viewModel.GoToLineCommand);
        registry.Register("Duplicate Line", "Edit", "Ctrl+Shift+D", viewModel.DuplicateLineCommand);
        registry.Register("Move Line Up", "Edit", "Alt+Up", viewModel.MoveLineUpCommand);
        registry.Register("Move Line Down", "Edit", "Alt+Down", viewModel.MoveLineDownCommand);
        registry.Register("Format Document", "Edit", null, viewModel.FormatDocumentCommand);
        registry.Register("Minify Document", "Edit", null, viewModel.MinifyDocumentCommand);
        registry.Register("Toggle Bookmark", "Edit", "Ctrl+F2", viewModel.ToggleBookmarkCommand);
        registry.Register("Next Bookmark", "Edit", "F2", viewModel.NextBookmarkCommand);
        registry.Register("Previous Bookmark", "Edit", "Shift+F2", viewModel.PrevBookmarkCommand);

        registry.Register("Toggle Word Wrap", "View", null, viewModel.ToggleWordWrapCommand);
        registry.Register("Show Whitespace", "View", null, viewModel.ToggleWhitespaceCommand);
        registry.Register("Toggle Code Folding", "View", null, viewModel.ToggleFoldingCommand);
        registry.Register("Toggle Minimap", "View", null, viewModel.ToggleMinimapCommand);
        registry.Register("Toggle Indent Guides", "View", null, viewModel.ToggleIndentGuidesCommand);
        registry.Register("Zoom In", "View", "Ctrl++", viewModel.ZoomInCommand);
        registry.Register("Zoom Out", "View", "Ctrl+-", viewModel.ZoomOutCommand);
        registry.Register("Reset Zoom", "View", "Ctrl+0", viewModel.ResetZoomCommand);
        registry.Register("Split Editor", "View", "Ctrl+\\", viewModel.ToggleSplitViewCommand);
        registry.Register("Side-by-Side View", "View", null, viewModel.ToggleSideBySideCommand);
        registry.Register("Toggle Terminal", "View", "Ctrl+`", viewModel.ToggleCommandRunnerCommand);
        registry.Register("Zen Mode", "View", "F11", viewModel.ToggleZenModeCommand);
        registry.Register("Toggle Explorer", "View", "Ctrl+B", viewModel.ToggleExplorerCommand);
        registry.Register("Auto-Reload", "View", null, viewModel.ToggleAutoReloadCommand);
        registry.Register("Compare Files", "View", null, viewModel.CompareFilesCommand);
        registry.Register("Show Completion", "Edit", "Ctrl+Space", viewModel.ShowCompletionCommand);

        registry.Register("UPPERCASE", "Text Tools", null, viewModel.TextToUpperCaseCommand);
        registry.Register("lowercase", "Text Tools", null, viewModel.TextToLowerCaseCommand);
        registry.Register("Title Case", "Text Tools", null, viewModel.TextToTitleCaseCommand);
        registry.Register("Invert Case", "Text Tools", null, viewModel.TextInvertCaseCommand);
        registry.Register("Remove Duplicate Lines", "Text Tools", null, viewModel.TextRemoveDuplicateLinesCommand);
        registry.Register("Sort Lines (A\u2192Z)", "Text Tools", null, viewModel.TextSortLinesAscCommand);
        registry.Register("Sort Lines (Z\u2192A)", "Text Tools", null, viewModel.TextSortLinesDescCommand);
        registry.Register("Trim Trailing Whitespace", "Text Tools", null, viewModel.TextTrimTrailingCommand);
        registry.Register("Trim Leading Whitespace", "Text Tools", null, viewModel.TextTrimLeadingCommand);
        registry.Register("Trim All Whitespace", "Text Tools", null, viewModel.TextTrimAllCommand);
        registry.Register("Tabs \u2192 Spaces", "Text Tools", null, viewModel.TextTabsToSpacesCommand);
        registry.Register("Spaces \u2192 Tabs", "Text Tools", null, viewModel.TextSpacesToTabsCommand);
        registry.Register("Base64 Encode", "Text Tools", null, viewModel.TextBase64EncodeCommand);
        registry.Register("Base64 Decode", "Text Tools", null, viewModel.TextBase64DecodeCommand);
        registry.Register("URL Encode", "Text Tools", null, viewModel.TextUrlEncodeCommand);
        registry.Register("URL Decode", "Text Tools", null, viewModel.TextUrlDecodeCommand);
        registry.Register("Checksum: MD5", "Text Tools", null, viewModel.TextChecksumMd5Command);
        registry.Register("Checksum: SHA-1", "Text Tools", null, viewModel.TextChecksumSha1Command);
        registry.Register("Checksum: SHA-256", "Text Tools", null, viewModel.TextChecksumSha256Command);
        registry.Register("Checksum: SHA-512", "Text Tools", null, viewModel.TextChecksumSha512Command);

        return registry;
    }
}
