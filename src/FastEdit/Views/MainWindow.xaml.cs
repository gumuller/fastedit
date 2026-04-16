using System.Windows;
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

        // Subscribe to events
        _viewModel.GoToLineRequested += OnGoToLineRequested;
        _viewModel.FindInFilesRequested += OnFindInFilesRequested;
        _viewModel.CompareFilesRequested += OnCompareFilesRequested;
        _viewModel.CommandPaletteRequested += OnCommandPaletteRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Setup Find in Files
        _findInFilesVm = App.Services.GetRequiredService<FindInFilesViewModel>();
        _findInFilesVm.NavigateToResult += OnNavigateToSearchResult;
        FindInFilesPanel.DataContext = _findInFilesVm;

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
        _commandRegistry.Register("Toggle Terminal", "View", "Ctrl+`", _viewModel.ToggleCommandRunnerCommand);
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
            SaveEditorState();
            SaveWindowState();
            _viewModel.SaveSession();
            Close();
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

    private void About_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.MessageBox.Show(
            "FastEdit v2.0\n\nA fast, lightweight text and hex editor for Windows.\n\nFeatures:\n- Syntax highlighting for 20+ languages\n- Hex editing for binary files\n- 9 built-in themes\n- Virtual scrolling for large files\n- Session restore with cursor position\n- Find & Replace, Find in Files\n- Command Palette (Ctrl+Shift+P)\n- Auto-complete (Ctrl+Space)\n- Code folding, indent guides\n- Git branch detection\n- Built-in terminal\n- Split editor view\n- File comparison",
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
                CommandRunner.FocusInput();
            }
            else
            {
                _savedTerminalHeight = TerminalRow.Height;
                TerminalRow.Height = new GridLength(0);
                TerminalRow.MinHeight = 0;
            }
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

    private Point? _dragStartPoint;
    private EditorTabViewModel? _dragSourceTab;

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
            _dragStartPoint = null;
            _dragSourceTab = null;
        }
    }

    private void TabHeader_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("TabItem"))
            e.Effects = DragDropEffects.Move;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void TabHeader_Drop(object sender, DragEventArgs e)
    {
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
