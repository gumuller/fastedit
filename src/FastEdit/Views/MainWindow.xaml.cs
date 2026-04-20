using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FastEdit.Infrastructure;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;
using FastEdit.Views.Controls;
using FastEdit.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;

namespace FastEdit.Views;

public partial class MainWindow : FluentWindow
{
    private MainViewModel? _viewModel;
    private FindInFilesViewModel? _findInFilesVm;
    private CommandRegistry? _commandRegistry;

    // Zen mode state
    private WindowState _preZenWindowState;
    private WindowStyle _preZenWindowStyle;
    private GridLength _preZenFileTreeWidth;
    private GridLength _savedExplorerWidth = new GridLength(250);

    public MainWindow()
    {
        InitializeComponent();

        // Set window icon
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            var dir = System.IO.Path.GetDirectoryName(exePath) ?? ".";
            var icoPath = System.IO.Path.Combine(dir, "fastedit.ico");
            if (System.IO.File.Exists(icoPath))
            {
                // Load icon at desired decode size for crisp rendering
                var windowIcon = new System.Windows.Media.Imaging.BitmapImage();
                windowIcon.BeginInit();
                windowIcon.UriSource = new Uri(icoPath, UriKind.Absolute);
                windowIcon.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                windowIcon.EndInit();
                Icon = windowIcon;

                // Load a separate decode for the title bar at higher resolution
                var titleIcon = new System.Windows.Media.Imaging.BitmapImage();
                titleIcon.BeginInit();
                titleIcon.UriSource = new Uri(icoPath, UriKind.Absolute);
                titleIcon.DecodePixelWidth = 48;
                titleIcon.DecodePixelHeight = 48;
                titleIcon.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                titleIcon.EndInit();
                AppIcon.Source = titleIcon;
            }
        }
        catch { /* icon is optional */ }

        _viewModel = App.Services.GetRequiredService<MainViewModel>();
        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        // Subscribe to events
        _viewModel.GoToLineRequested += OnGoToLineRequested;
        _viewModel.FindInFilesRequested += OnFindInFilesRequested;
        _viewModel.CompareFilesRequested += OnCompareFilesRequested;
        _viewModel.CommandPaletteRequested += OnCommandPaletteRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.FindRequested += OnFindRequested;
        _viewModel.ReplaceRequested += OnReplaceRequested;

        // Setup Find in Files
        _findInFilesVm = App.Services.GetRequiredService<FindInFilesViewModel>();
        _findInFilesVm.NavigateToResult += OnNavigateToSearchResult;
        FindInFilesPanel.DataContext = _findInFilesVm;

        // Setup Line Filter Panel
        _viewModel.ToggleFilterPanelRequested += OnToggleFilterPanel;
        LineFilterPanel.CloseRequested += () =>
        {
            if (_viewModel != null) _viewModel.IsFilterPanelVisible = false;
            LineFilterPanel.Visibility = Visibility.Collapsed;
        };
        LineFilterPanel.NavigateNextRequested += () =>
        {
            var lfv = FindActiveLargeFileViewer();
            if (lfv != null) lfv.NavigateToNextFilterMatch();
            else FindActiveEditorHost()?.NavigateToNextFilterMatch();
        };
        LineFilterPanel.NavigatePrevRequested += () =>
        {
            var lfv = FindActiveLargeFileViewer();
            if (lfv != null) lfv.NavigateToPreviousFilterMatch();
            else FindActiveEditorHost()?.NavigateToPreviousFilterMatch();
        };
        // LargeFileViewer subscribes to the filter service itself via App.Services;
        // no need for MainWindow to re-dispatch FiltersUpdated.

        BuildCommandRegistry();
    }

    private void BuildCommandRegistry()
    {
        if (_viewModel == null) return;
        _commandRegistry = new CommandRegistry();

        _commandRegistry.Register("New File", "File", "Ctrl+N", _viewModel.NewFileCommand);
        _commandRegistry.Register("Open File", "File", "Ctrl+O", _viewModel.OpenFileCommand);
        _commandRegistry.Register("Open Folder", "File", null, _viewModel.OpenFolderCommand);
        _commandRegistry.Register("Add Folder to Workspace", "File", null, _viewModel.AddFolderCommand);
        _commandRegistry.Register("Open Workspace", "File", null, _viewModel.OpenWorkspaceCommand);
        _commandRegistry.Register("Save Workspace As", "File", null, _viewModel.SaveWorkspaceCommand);
        _commandRegistry.Register("Save Session As", "File", null, _viewModel.SaveSessionAsCommand);
        _commandRegistry.Register("Save", "File", "Ctrl+S", _viewModel.SaveCommand);
        _commandRegistry.Register("Save As", "File", "Ctrl+Shift+S", _viewModel.SaveAsCommand);
        _commandRegistry.Register("Print", "File", "Ctrl+P", _viewModel.PrintCommand);
        _commandRegistry.Register("Select Next Occurrence", "Edit", "Ctrl+D", _viewModel.SelectNextOccurrenceCommand);
        _commandRegistry.Register("Select All Occurrences", "Edit", "Ctrl+Shift+L", _viewModel.SelectAllOccurrencesCommand);
        _commandRegistry.Register("Start Macro Recording", "Edit", null, _viewModel.MacroStartRecordingCommand);
        _commandRegistry.Register("Stop Macro Recording", "Edit", null, _viewModel.MacroStopRecordingCommand);
        _commandRegistry.Register("Playback Macro", "Edit", null, _viewModel.MacroPlaybackCommand);
        _commandRegistry.Register("Playback Macro Multiple", "Edit", null, _viewModel.MacroPlaybackMultipleCommand);
        _commandRegistry.Register("Close Tab", "File", "Ctrl+W", _viewModel.CloseTabCommand);

        _commandRegistry.Register("Find", "Edit", "Ctrl+F", _viewModel.FindCommand);
        _commandRegistry.Register("Replace", "Edit", "Ctrl+H", _viewModel.ReplaceCommand);
        _commandRegistry.Register("Find in Files", "Edit", "Ctrl+Shift+F", _viewModel.FindInFilesCommand);
        _commandRegistry.Register("Go to Line", "Edit", "Ctrl+G", _viewModel.GoToLineCommand);
        _commandRegistry.Register("Duplicate Line", "Edit", "Ctrl+Shift+D", _viewModel.DuplicateLineCommand);
        _commandRegistry.Register("Move Line Up", "Edit", "Alt+Up", _viewModel.MoveLineUpCommand);
        _commandRegistry.Register("Move Line Down", "Edit", "Alt+Down", _viewModel.MoveLineDownCommand);
        _commandRegistry.Register("Format Document", "Edit", null, _viewModel.FormatDocumentCommand);
        _commandRegistry.Register("Minify Document", "Edit", null, _viewModel.MinifyDocumentCommand);
        _commandRegistry.Register("Toggle Bookmark", "Edit", "Ctrl+F2", _viewModel.ToggleBookmarkCommand);
        _commandRegistry.Register("Next Bookmark", "Edit", "F2", _viewModel.NextBookmarkCommand);
        _commandRegistry.Register("Previous Bookmark", "Edit", "Shift+F2", _viewModel.PrevBookmarkCommand);

        _commandRegistry.Register("Toggle Word Wrap", "View", null, _viewModel.ToggleWordWrapCommand);
        _commandRegistry.Register("Show Whitespace", "View", null, _viewModel.ToggleWhitespaceCommand);
        _commandRegistry.Register("Toggle Code Folding", "View", null, _viewModel.ToggleFoldingCommand);
        _commandRegistry.Register("Toggle Minimap", "View", null, _viewModel.ToggleMinimapCommand);
        _commandRegistry.Register("Toggle Indent Guides", "View", null, _viewModel.ToggleIndentGuidesCommand);
        _commandRegistry.Register("Zoom In", "View", "Ctrl++", _viewModel.ZoomInCommand);
        _commandRegistry.Register("Zoom Out", "View", "Ctrl+-", _viewModel.ZoomOutCommand);
        _commandRegistry.Register("Reset Zoom", "View", "Ctrl+0", _viewModel.ResetZoomCommand);
        _commandRegistry.Register("Split Editor", "View", "Ctrl+\\", _viewModel.ToggleSplitViewCommand);
        _commandRegistry.Register("Side-by-Side View", "View", null, _viewModel.ToggleSideBySideCommand);
        _commandRegistry.Register("Toggle Terminal", "View", "Ctrl+`", _viewModel.ToggleCommandRunnerCommand);
        _commandRegistry.Register("Zen Mode", "View", "F11", _viewModel.ToggleZenModeCommand);
        _commandRegistry.Register("Toggle Explorer", "View", "Ctrl+B", _viewModel.ToggleExplorerCommand);
        _commandRegistry.Register("Auto-Reload", "View", null, _viewModel.ToggleAutoReloadCommand);
        _commandRegistry.Register("Compare Files", "View", null, _viewModel.CompareFilesCommand);
        _commandRegistry.Register("Show Completion", "Edit", "Ctrl+Space", _viewModel.ShowCompletionCommand);

        // Text Tools
        _commandRegistry.Register("UPPERCASE", "Text Tools", null, _viewModel.TextToUpperCaseCommand);
        _commandRegistry.Register("lowercase", "Text Tools", null, _viewModel.TextToLowerCaseCommand);
        _commandRegistry.Register("Title Case", "Text Tools", null, _viewModel.TextToTitleCaseCommand);
        _commandRegistry.Register("Invert Case", "Text Tools", null, _viewModel.TextInvertCaseCommand);
        _commandRegistry.Register("Remove Duplicate Lines", "Text Tools", null, _viewModel.TextRemoveDuplicateLinesCommand);
        _commandRegistry.Register("Sort Lines (A→Z)", "Text Tools", null, _viewModel.TextSortLinesAscCommand);
        _commandRegistry.Register("Sort Lines (Z→A)", "Text Tools", null, _viewModel.TextSortLinesDescCommand);
        _commandRegistry.Register("Trim Trailing Whitespace", "Text Tools", null, _viewModel.TextTrimTrailingCommand);
        _commandRegistry.Register("Trim Leading Whitespace", "Text Tools", null, _viewModel.TextTrimLeadingCommand);
        _commandRegistry.Register("Trim All Whitespace", "Text Tools", null, _viewModel.TextTrimAllCommand);
        _commandRegistry.Register("Tabs → Spaces", "Text Tools", null, _viewModel.TextTabsToSpacesCommand);
        _commandRegistry.Register("Spaces → Tabs", "Text Tools", null, _viewModel.TextSpacesToTabsCommand);
        _commandRegistry.Register("Base64 Encode", "Text Tools", null, _viewModel.TextBase64EncodeCommand);
        _commandRegistry.Register("Base64 Decode", "Text Tools", null, _viewModel.TextBase64DecodeCommand);
        _commandRegistry.Register("URL Encode", "Text Tools", null, _viewModel.TextUrlEncodeCommand);
        _commandRegistry.Register("URL Decode", "Text Tools", null, _viewModel.TextUrlDecodeCommand);
        _commandRegistry.Register("Checksum: MD5", "Text Tools", null, _viewModel.TextChecksumMd5Command);
        _commandRegistry.Register("Checksum: SHA-1", "Text Tools", null, _viewModel.TextChecksumSha1Command);
        _commandRegistry.Register("Checksum: SHA-256", "Text Tools", null, _viewModel.TextChecksumSha256Command);
        _commandRegistry.Register("Checksum: SHA-512", "Text Tools", null, _viewModel.TextChecksumSha512Command);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Restore window position and size
        var settings = App.Services.GetRequiredService<ISettingsService>();
        if (!double.IsNaN(settings.WindowLeft) && !double.IsNaN(settings.WindowTop))
        {
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
        }
        if (settings.WindowWidth > 0) Width = settings.WindowWidth;
        if (settings.WindowHeight > 0) Height = settings.WindowHeight;
        if (settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
            MaxRestoreIcon.Text = "\uE923";
            MaxRestoreButton.ToolTip = "Restore Down";
        }

        if (_viewModel != null)
        {
            // Check for crash recovery first
            var autoSave = App.Services.GetService<IAutoSaveService>();
            if (autoSave != null && autoSave.HasRecoveryFiles())
            {
                var dialogService = App.Services.GetRequiredService<IDialogService>();
                var result = dialogService.ShowMessage(
                    "FastEdit was not shut down cleanly. Would you like to recover unsaved files?",
                    "Crash Recovery",
                    DialogButtons.YesNo,
                    DialogIcon.Warning);

                if (result == Services.Interfaces.DialogResult.Yes)
                {
                    var entries = autoSave.GetRecoveryEntries();
                    foreach (var entry in entries)
                    {
                        var tab = _viewModel.RecoverTab(entry);
                        if (tab != null) _viewModel.Tabs.Add(tab);
                    }
                    if (_viewModel.Tabs.Count > 0)
                        _viewModel.SelectedTab = _viewModel.Tabs[0];
                }
                autoSave.ClearRecoveryFiles();
            }

            await _viewModel.RestoreSessionAsync();

            // Set command runner working directory
            var folder = _viewModel.FileTree.RootPath;
            if (!string.IsNullOrEmpty(folder))
                CommandRunner.SetWorkingDirectory(folder);
        }

        // Apply custom key bindings
        ApplyKeyBindings();
    }

    private bool _isClosingConfirmed;

    private void SaveWindowState()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            settings.WindowLeft = Left;
            settings.WindowTop = Top;
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
        }
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_viewModel == null) return;

        if (_isClosingConfirmed)
        {
            SaveEditorState();
            SaveWindowState();
            _viewModel.SaveSession();
            return;
        }

        if (!_viewModel.HasUnsavedChanges())
        {
            _isClosingConfirmed = true;
            SaveEditorState();
            SaveWindowState();
            _viewModel.SaveSession();
            return;
        }

        e.Cancel = true;

        var canClose = await _viewModel.ConfirmExitAsync();
        if (canClose)
        {
            _isClosingConfirmed = true;
            // Defer the Close() call — calling it synchronously from inside an
            // async-void Closing handler (even after the await) can race with
            // WPF's internal "window is closing" state and throw
            // "Cannot set Visibility / call Close … while a Window is closing".
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SaveEditorState();
                SaveWindowState();
                _viewModel.SaveSession();
                Close();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void SaveEditorState()
    {
        // Save cursor/scroll offsets from active editors before session save
        var editorHost = FindActiveEditorHost();
        editorHost?.SaveStateToViewModel();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsDialog();
    }

    private void ToggleExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.IsExplorerVisible = !_viewModel.IsExplorerVisible;
    }

    private void OnToggleFilterPanel()
    {
        LineFilterPanel.Visibility = _viewModel?.IsFilterPanelVisible == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnOpenSettingsRequested()
    {
        ShowSettingsDialog();
    }

    private void ShowSettingsDialog()
    {
        if (_viewModel == null) return;
        var settingsService = App.Services.GetRequiredService<ISettingsService>();
        var keyBindingService = App.Services.GetRequiredService<IKeyBindingService>();

        var dialog = new SettingsWindow(_viewModel, settingsService, keyBindingService)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.ShortcutsChanged)
        {
            ApplyKeyBindings();
        }
    }

    private void ApplyKeyBindings()
    {
        if (_viewModel == null) return;
        var keyBindingService = App.Services.GetRequiredService<IKeyBindingService>();
        var bindings = keyBindingService.GetBindings();

        InputBindings.Clear();

        var commandMap = new Dictionary<string, ICommand>
        {
            ["NewFile"] = _viewModel.NewFileCommand,
            ["OpenFile"] = _viewModel.OpenFileCommand,
            ["Save"] = _viewModel.SaveCommand,
            ["SaveAs"] = _viewModel.SaveAsCommand,
            ["CloseTab"] = _viewModel.CloseTabCommand,
            ["Find"] = _viewModel.FindCommand,
            ["Replace"] = _viewModel.ReplaceCommand,
            ["GoToLine"] = _viewModel.GoToLineCommand,
            ["DuplicateLine"] = _viewModel.DuplicateLineCommand,
            ["MoveLineUp"] = _viewModel.MoveLineUpCommand,
            ["MoveLineDown"] = _viewModel.MoveLineDownCommand,
            ["ZoomIn"] = _viewModel.ZoomInCommand,
            ["ZoomOut"] = _viewModel.ZoomOutCommand,
            ["ResetZoom"] = _viewModel.ResetZoomCommand,
            ["FindInFiles"] = _viewModel.FindInFilesCommand,
            ["ToggleBookmark"] = _viewModel.ToggleBookmarkCommand,
            ["NextBookmark"] = _viewModel.NextBookmarkCommand,
            ["PrevBookmark"] = _viewModel.PrevBookmarkCommand,
            ["CommandPalette"] = _viewModel.CommandPaletteCommand,
            ["Completion"] = _viewModel.ShowCompletionCommand,
            ["ToggleTerminal"] = _viewModel.ToggleCommandRunnerCommand,
            ["SplitView"] = _viewModel.ToggleSplitViewCommand,
            ["Print"] = _viewModel.PrintCommand,
            ["SelectNextOccurrence"] = _viewModel.SelectNextOccurrenceCommand,
            ["SelectAllOccurrences"] = _viewModel.SelectAllOccurrencesCommand,
            ["Settings"] = _viewModel.OpenSettingsCommand,
            ["ZenMode"] = _viewModel.ToggleZenModeCommand,
            ["ToggleExplorer"] = _viewModel.ToggleExplorerCommand,
            ["ToggleFilterPanel"] = _viewModel.ToggleFilterPanelCommand,
        };

        foreach (var kvp in bindings)
        {
            if (!commandMap.TryGetValue(kvp.Key, out var command)) continue;

            try
            {
                var gesture = ParseGesture(kvp.Value);
                if (gesture != null)
                    InputBindings.Add(new KeyBinding(command, gesture));
            }
            catch
            {
                // Skip invalid gestures
            }
        }
    }

    private static KeyGesture? ParseGesture(string gestureString)
    {
        var modifiers = ModifierKeys.None;
        var parts = gestureString.Split('+');
        var keyPart = parts[^1].Trim();

        for (int i = 0; i < parts.Length - 1; i++)
        {
            var mod = parts[i].Trim();
            if (mod.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) modifiers |= ModifierKeys.Control;
            else if (mod.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= ModifierKeys.Alt;
            else if (mod.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= ModifierKeys.Shift;
        }

        var key = keyPart switch
        {
            "Plus" => Key.OemPlus,
            "Minus" => Key.OemMinus,
            "`" => Key.OemTilde,
            "\\" => Key.OemPipe,
            "," => Key.OemComma,
            "0" => Key.D0,
            "1" => Key.D1,
            "2" => Key.D2,
            "3" => Key.D3,
            "4" => Key.D4,
            "5" => Key.D5,
            "6" => Key.D6,
            "7" => Key.D7,
            "8" => Key.D8,
            "9" => Key.D9,
            _ => Enum.TryParse<Key>(keyPart, true, out var k) ? k : (Key?)null
        };

        if (key == null) return null;

        try
        {
            return new KeyGesture(key.Value, modifiers);
        }
        catch
        {
            return null;
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        System.Windows.MessageBox.Show(
            $"FastEdit v{versionStr}\n\nA fast, lightweight text and hex editor for Windows.\n\nFeatures:\n- Syntax highlighting for 20+ languages\n- Hex editing for binary files\n- 9 built-in themes\n- Virtual scrolling for large files\n- Session restore with cursor position\n- Find & Replace, Find in Files\n- Command Palette (Ctrl+Shift+P)\n- Auto-complete (Ctrl+Space)\n- Code folding, indent guides\n- Git branch detection\n- Built-in terminal\n- Split editor view\n- File comparison",
            "About FastEdit",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private void FileTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileTreeView.SelectedItem is FileNodeViewModel node && !node.IsDirectory)
        {
            _viewModel?.OpenFileCommand.Execute(node.FullPath);
        }
    }

    // --- Go To Line Dialog ---
    private void OnGoToLineRequested(int currentLine)
    {
        var dialog = new GoToLineDialog(currentLine);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.LineNumber > 0)
        {
            var editorHost = FindActiveEditorHost();
            editorHost?.GoToLine(dialog.LineNumber);
        }
    }

    // --- Command Palette ---
    private void OnCommandPaletteRequested()
    {
        if (_commandRegistry == null) return;

        var palette = new CommandPaletteWindow(_commandRegistry)
        {
            Owner = this
        };

        if (palette.ShowDialog() == true && palette.SelectedCommand != null)
        {
            var cmd = palette.SelectedCommand;
            if (cmd.Command.CanExecute(cmd.CommandParameter))
                cmd.Command.Execute(cmd.CommandParameter);
        }
    }

    // --- Drag & Drop ---
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (_viewModel == null) return;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            foreach (var file in files)
            {
                if (System.IO.File.Exists(file))
                {
                    await _viewModel.OpenFileCommand.ExecuteAsync(file);
                }
                else if (System.IO.Directory.Exists(file))
                {
                    _viewModel.FileTree.OpenFolderCommand.Execute(file);
                    CommandRunner.SetWorkingDirectory(file);
                }
            }
        }
    }

    // --- Find in Files ---
    private void OnFindInFilesRequested()
    {
        if (_findInFilesVm != null)
        {
            _findInFilesVm.FolderPath = _viewModel?.FileTree.RootPath;
        }
        FindInFilesPanel.Visibility = Visibility.Visible;
        FindInFilesPanel.FocusSearch();
    }

    private async void OnNavigateToSearchResult(object? sender, (string filePath, int line) result)
    {
        if (_viewModel == null) return;
        await _viewModel.OpenFileCommand.ExecuteAsync(result.filePath);

        // Give UI time to load the tab
        await System.Threading.Tasks.Task.Delay(100);
        Dispatcher.Invoke(() =>
        {
            var editorHost = FindActiveEditorHost();
            editorHost?.GoToLine(result.line);
        });
    }

    // --- Terminal row visibility ---
    private GridLength _savedTerminalHeight = new(250);

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsCommandRunnerVisible))
        {
            if (_viewModel!.IsCommandRunnerVisible)
            {
                TerminalRow.Height = _savedTerminalHeight;
                TerminalRow.MinHeight = 80;
                CommandRunner.EnsureStarted();
                Dispatcher.BeginInvoke(() => CommandRunner.FocusInput(),
                    System.Windows.Threading.DispatcherPriority.Input);
            }
            else
            {
                _savedTerminalHeight = TerminalRow.Height;
                TerminalRow.Height = new GridLength(0);
                TerminalRow.MinHeight = 0;
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.IsZenMode))
        {
            if (_viewModel!.IsZenMode)
                EnterZenMode();
            else
                ExitZenMode();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsSideBySideMode))
        {
            UpdateSideBySideColumns();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsExplorerVisible))
        {
            ToggleExplorerPanel(_viewModel!.IsExplorerVisible);
        }
    }

    private void UpdateSideBySideColumns()
    {
        if (_viewModel == null) return;

        if (_viewModel.IsSideBySideMode)
        {
            SideBySideSplitterCol.Width = GridLength.Auto;
            SecondaryEditorCol.Width = new GridLength(1, GridUnitType.Star);
            SideBySideSplitter.Visibility = Visibility.Visible;
        }
        else
        {
            SideBySideSplitterCol.Width = new GridLength(0);
            SecondaryEditorCol.Width = new GridLength(0);
            SideBySideSplitter.Visibility = Visibility.Collapsed;
        }
    }

    private void EnterZenMode()
    {
        _preZenWindowState = WindowState;
        _preZenWindowStyle = WindowStyle;
        _preZenFileTreeWidth = FileTreeColumn.Width;

        // Hide chrome
        MenuBar.Visibility = Visibility.Collapsed;
        FileTreePanel.Visibility = Visibility.Collapsed;
        TreeSplitter.Visibility = Visibility.Collapsed;
        MainStatusBar.Visibility = Visibility.Collapsed;
        TitleBarGrid.Visibility = Visibility.Collapsed;
        FileTreeColumn.Width = new GridLength(0);
        FileTreeColumn.MinWidth = 0;
        FileTreeColumn.MaxWidth = 0;

        // Hide terminal if visible
        if (_viewModel!.IsCommandRunnerVisible)
            _viewModel.IsCommandRunnerVisible = false;

        // Fullscreen
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;
    }

    private void ExitZenMode()
    {
        // Restore chrome
        MenuBar.Visibility = Visibility.Visible;
        FileTreePanel.Visibility = Visibility.Visible;
        TreeSplitter.Visibility = Visibility.Visible;
        MainStatusBar.Visibility = Visibility.Visible;
        TitleBarGrid.Visibility = Visibility.Visible;
        FileTreeColumn.Width = _preZenFileTreeWidth;
        FileTreeColumn.MinWidth = 0;
        FileTreeColumn.MaxWidth = 400;

        // Restore window state
        WindowStyle = _preZenWindowStyle;
        WindowState = _preZenWindowState;
    }

    private void ToggleExplorerPanel(bool visible)
    {
        if (visible)
        {
            FileTreePanel.Visibility = Visibility.Visible;
            TreeSplitter.Visibility = Visibility.Visible;
            FileTreeColumn.Width = _savedExplorerWidth;
            FileTreeColumn.MaxWidth = 400;
        }
        else
        {
            _savedExplorerWidth = FileTreeColumn.Width;
            FileTreePanel.Visibility = Visibility.Collapsed;
            TreeSplitter.Visibility = Visibility.Collapsed;
            FileTreeColumn.Width = new GridLength(0);
            FileTreeColumn.MaxWidth = 0;
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _viewModel?.IsZenMode == true)
        {
            _viewModel.IsZenMode = false;
            e.Handled = true;
        }
    }

    // --- Compare Files ---
    private void OnCompareFilesRequested()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select two files to compare (hold Ctrl to multi-select)",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true) return;

        string leftPath, rightPath;

        if (dialog.FileNames.Length >= 2)
        {
            leftPath = dialog.FileNames[0];
            rightPath = dialog.FileNames[1];
        }
        else if (dialog.FileNames.Length == 1)
        {
            leftPath = dialog.FileNames[0];
            // Ask for second file
            var dialog2 = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select second file to compare",
                Multiselect = false,
                InitialDirectory = System.IO.Path.GetDirectoryName(leftPath)
            };
            if (dialog2.ShowDialog() != true) return;
            rightPath = dialog2.FileName;
        }
        else return;

        var compareWindow = new CompareFilesWindow
        {
            Owner = this
        };
        compareWindow.CompareFiles(leftPath, rightPath);
        compareWindow.Show();
    }

    // --- Helper: Find active EditorHost in visual tree ---
    private EditorHost? FindActiveEditorHost()
    {
        return FindVisualChild<EditorHost>(this);
    }

    private LargeFileViewer? FindActiveLargeFileViewer()
    {
        return FindVisualChild<LargeFileViewer>(this);
    }

    private HexEditorControl? FindActiveHexEditor()
    {
        return FindVisualChild<HexEditorControl>(this);
    }

    private void OnFindRequested()
    {
        // EditorHost subscribes to FindRequested on its own for normal text mode.
        // Route to the large-file viewer or hex editor when appropriate.
        var lfv = FindActiveLargeFileViewer();
        if (lfv != null) { lfv.ShowFindBar(focusSearch: true); return; }

        var hex = FindActiveHexEditor();
        if (hex != null) { hex.ShowSearch(); return; }
    }

    private void OnReplaceRequested()
    {
        // LargeFileViewer is read-only; nothing to do. EditorHost handles its own case.
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : UIElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result && result.Visibility == Visibility.Visible)
                return result;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void AppIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SystemCommands.ShowSystemMenu(this, PointToScreen(new Point(0, TitleBarGrid.ActualHeight)));
    }

    private void TabHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is EditorTabViewModel tab && _viewModel != null)
        {
            _viewModel.SelectedTab = tab;
            _dragStartPoint = e.GetPosition(null);
            _dragSourceTab = tab;
            e.Handled = true;
        }
    }

    private void TabHeader_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is EditorTabViewModel tab && _viewModel != null)
        {
            var contextMenu = new ContextMenu();

            var splitItem = new System.Windows.Controls.MenuItem { Header = "Open in Split View" };
            splitItem.Click += (_, _) =>
            {
                _viewModel.OpenInSplitView(tab);
                UpdateSideBySideColumns();
            };
            contextMenu.Items.Add(splitItem);

            if (_viewModel.IsSideBySideMode)
            {
                var closeItem = new System.Windows.Controls.MenuItem { Header = "Close Split View" };
                closeItem.Click += (_, _) =>
                {
                    _viewModel.CloseSideBySide();
                    UpdateSideBySideColumns();
                };
                contextMenu.Items.Add(closeItem);
            }

            fe.ContextMenu = contextMenu;
            contextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private Point? _dragStartPoint;
    private EditorTabViewModel? _dragSourceTab;
    private TabDropAdorner? _tabDropAdorner;

    private void TabHeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStartPoint == null || _dragSourceTab == null)
            return;

        var pos = e.GetPosition(null);
        var diff = pos - _dragStartPoint.Value;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var data = new DataObject("TabItem", _dragSourceTab);
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
            RemoveTabDropAdorner();
            _dragStartPoint = null;
            _dragSourceTab = null;
        }
    }

    private void TabHeader_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("TabItem"))
        {
            e.Effects = DragDropEffects.Move;
            UpdateTabDropAdorner(sender as FrameworkElement, e);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void UpdateTabDropAdorner(FrameworkElement? targetElement, DragEventArgs e)
    {
        if (targetElement == null) return;

        var adornerLayer = AdornerLayer.GetAdornerLayer(TabStrip);
        if (adornerLayer == null) return;

        if (_tabDropAdorner == null)
        {
            _tabDropAdorner = new TabDropAdorner(TabStrip);
            adornerLayer.Add(_tabDropAdorner);
        }

        var pos = e.GetPosition(TabStrip);
        var targetCenter = targetElement.TranslatePoint(
            new Point(targetElement.ActualWidth / 2, 0), TabStrip).X;

        double insertX;
        if (pos.X < targetCenter)
        {
            insertX = targetElement.TranslatePoint(new Point(0, 0), TabStrip).X;
        }
        else
        {
            insertX = targetElement.TranslatePoint(new Point(targetElement.ActualWidth, 0), TabStrip).X;
        }

        _tabDropAdorner.UpdatePosition(insertX);
    }

    private void RemoveTabDropAdorner()
    {
        if (_tabDropAdorner != null)
        {
            var adornerLayer = AdornerLayer.GetAdornerLayer(TabStrip);
            adornerLayer?.Remove(_tabDropAdorner);
            _tabDropAdorner = null;
        }
    }

    private void TabHeader_DragLeave(object sender, DragEventArgs e)
    {
        var pos = e.GetPosition(TabStrip);
        if (pos.X < 0 || pos.Y < 0 || pos.X > TabStrip.ActualWidth || pos.Y > TabStrip.ActualHeight)
        {
            RemoveTabDropAdorner();
        }
    }

    private void TabHeader_Drop(object sender, DragEventArgs e)
    {
        RemoveTabDropAdorner();

        if (_viewModel == null || !e.Data.GetDataPresent("TabItem")) return;

        var sourceTab = e.Data.GetData("TabItem") as EditorTabViewModel;
        var targetTab = (sender as FrameworkElement)?.Tag as EditorTabViewModel;

        if (sourceTab == null || targetTab == null || sourceTab == targetTab) return;

        var sourceIndex = _viewModel.Tabs.IndexOf(sourceTab);
        var targetIndex = _viewModel.Tabs.IndexOf(targetTab);

        if (sourceIndex >= 0 && targetIndex >= 0)
        {
            _viewModel.Tabs.Move(sourceIndex, targetIndex);
        }
    }

    private void TabClose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is EditorTabViewModel tab)
        {
            _viewModel?.CloseTabCommand.Execute(tab);
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            MaxRestoreIcon.Text = "\uE922"; // Maximize icon
            MaxRestoreButton.ToolTip = "Maximize";
        }
        else
        {
            WindowState = WindowState.Maximized;
            MaxRestoreIcon.Text = "\uE923"; // Restore icon
            MaxRestoreButton.ToolTip = "Restore Down";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
