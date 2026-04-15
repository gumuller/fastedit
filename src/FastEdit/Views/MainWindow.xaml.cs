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
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(icoPath, UriKind.Absolute);
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                Icon = bitmap;
                AppIcon.Source = bitmap;
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
        _commandRegistry.Register("Save", "File", "Ctrl+S", _viewModel.SaveCommand);
        _commandRegistry.Register("Save As", "File", "Ctrl+Shift+S", _viewModel.SaveAsCommand);
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

    // --- Compare Files ---
    private void OnCompareFilesRequested()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select first file to compare",
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;
        var leftPath = dialog.FileName;

        dialog.Title = "Select second file to compare";
        if (dialog.ShowDialog() != true) return;
        var rightPath = dialog.FileName;

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
            e.Handled = true;
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
