using System.Diagnostics;
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
using Wpf.Ui.Controls;

namespace FastEdit.Views;

public partial class MainWindow : FluentWindow
{
    private MainViewModel? _viewModel;
    private FindInFilesViewModel? _findInFilesVm;
    private readonly ISettingsService _settingsService;
    private readonly IKeyBindingService _keyBindingService;
    private readonly IAutoSaveService _autoSaveService;
    private readonly IDialogService _dialogService;
    private readonly IFileSystemService _fileSystemService;
    private CommandRegistry? _commandRegistry;

    // Zen mode state
    private WindowState _preZenWindowState;
    private WindowStyle _preZenWindowStyle;
    private GridLength _preZenFileTreeWidth;
    private GridLength _savedExplorerWidth = new GridLength(250);

    public MainViewModel MainViewModel { get; }
    public ISettingsService SettingsService => _settingsService;
    public IDialogService DialogService => _dialogService;
    public IFileSystemService FileSystemService => _fileSystemService;
    public ILineFilterService LineFilterService { get; }
    public IThemeService ThemeService { get; }
    public ITextToolsService TextToolsService { get; }
    public IMacroService MacroService { get; }

    public MainWindow(
        MainViewModel viewModel,
        FindInFilesViewModel findInFilesVm,
        ISettingsService settingsService,
        IKeyBindingService keyBindingService,
        IAutoSaveService autoSaveService,
        IDialogService dialogService,
        IFileSystemService fileSystemService,
        ILineFilterService lineFilterService,
        IThemeService themeService,
        ITextToolsService textToolsService,
        IMacroService macroService)
    {
        _viewModel = MainViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _findInFilesVm = findInFilesVm ?? throw new ArgumentNullException(nameof(findInFilesVm));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _keyBindingService = keyBindingService ?? throw new ArgumentNullException(nameof(keyBindingService));
        _autoSaveService = autoSaveService ?? throw new ArgumentNullException(nameof(autoSaveService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        LineFilterService = lineFilterService ?? throw new ArgumentNullException(nameof(lineFilterService));
        ThemeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        TextToolsService = textToolsService ?? throw new ArgumentNullException(nameof(textToolsService));
        MacroService = macroService ?? throw new ArgumentNullException(nameof(macroService));

        InitializeComponent();

        // Set window icon
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            var dir = System.IO.Path.GetDirectoryName(exePath) ?? ".";
            var icoPath = System.IO.Path.Combine(dir, "fastedit.ico");
            if (_fileSystemService.FileExists(icoPath))
            {
                // Force the window icon to decode from a SMALL frame of the
                // .ico. Without DecodePixelWidth, WPF picks the largest frame
                // (256x256 with its rounded-rect padding) and Windows then
                // scales that down for the taskbar — which looks tiny.
                // By requesting a 48px decode, WPF selects the 48x48 frame
                // (tight-cropped glyph) so the taskbar renders it densely.
                var windowIcon = new System.Windows.Media.Imaging.BitmapImage();
                windowIcon.BeginInit();
                windowIcon.UriSource = new Uri(icoPath, UriKind.Absolute);
                windowIcon.DecodePixelWidth = 48;
                windowIcon.DecodePixelHeight = 48;
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
        catch (Exception ex)
        {
            Trace.TraceWarning($"Optional app icon could not be loaded: {ex.Message}");
        }

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
        _findInFilesVm.NavigateToResult += OnNavigateToSearchResult;
        FindInFilesPanel.DataContext = _findInFilesVm;

        // Setup Line Filter Panel
        LineFilterPanel.SetServices(LineFilterService, DialogService);
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
        // LargeFileViewer subscribes to the filter service itself;
        // no need for MainWindow to re-dispatch FiltersUpdated.

        BuildCommandRegistry();
    }

    private void BuildCommandRegistry()
    {
        if (_viewModel == null) return;
        _commandRegistry = MainWindowCommandRegistryFactory.Create(_viewModel);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Restore window position and size
        var settings = _settingsService;
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
            if (CrashRecoveryStartupPolicy.ShouldPromptForRecovery(
                _autoSaveService.HasRecoveryFiles(),
                App.HasAnotherRunningInstance))
            {
                var result = _dialogService.ShowMessage(
                    "FastEdit was not shut down cleanly. Would you like to recover unsaved files?",
                    "Crash Recovery",
                    DialogButtons.YesNo,
                    DialogIcon.Warning);

                if (result == Services.Interfaces.DialogResult.Yes)
                {
                    var entries = _autoSaveService.GetRecoveryEntries();
                    foreach (var entry in entries)
                    {
                        var tab = _viewModel.RecoverTab(entry);
                        if (tab != null) _viewModel.Tabs.Add(tab);
                    }
                    if (_viewModel.Tabs.Count > 0)
                        _viewModel.SelectedTab = _viewModel.Tabs[0];
                }
                _autoSaveService.ClearRecoveryFiles();
            }

            await _viewModel.RestoreSessionAsync();

            // Open any files passed on the command line (e.g. Explorer "Open
            // with FastEdit"). Done after session restore so these files end
            // up on top and become the selected tab.
            foreach (var path in App.StartupFiles)
            {
                await _viewModel.OpenFileCommand.ExecuteAsync(path);
            }

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
        var settings = _settingsService;
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
            _ = Dispatcher.BeginInvoke(new Action(() =>
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
        var dialog = new SettingsWindow(_viewModel, _settingsService, _keyBindingService, _dialogService)
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
        var bindings = _keyBindingService.GetBindings();

        InputBindings.Clear();
        var commandMap = MainWindowKeyBindingFactory.CreateCommandMap(_viewModel);
        foreach (var keyBinding in MainWindowKeyBindingFactory.CreateKeyBindings(bindings, commandMap))
        {
            InputBindings.Add(keyBinding);
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Views.Dialogs.AboutDialog { Owner = this };
        dlg.ShowDialog();
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
        var dialog = new GoToLineDialog(currentLine, _dialogService);
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
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            foreach (var action in DroppedPathClassifier.Classify(paths, _fileSystemService))
            {
                switch (action.Kind)
                {
                    case DroppedPathKind.File:
                        await _viewModel.OpenFileCommand.ExecuteAsync(action.Path);
                        break;
                    case DroppedPathKind.Directory:
                        _viewModel.FileTree.OpenFolderCommand.Execute(action.Path);
                        CommandRunner.SetWorkingDirectory(action.Path);
                        break;
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
        var fileNames = _dialogService.ShowOpenFilesDialog();
        if (fileNames.Length == 0) return;

        string? secondPath = null;
        if (CompareFileSelectionResolver.NeedsSecondFile(fileNames))
        {
            secondPath = _dialogService.ShowOpenFileDialog(initialDirectory: _fileSystemService.GetDirectoryName(fileNames[0]));
            if (secondPath == null) return;
        }

        if (!CompareFileSelectionResolver.TryResolve(fileNames, secondPath, out var selection))
            return;

        var compareWindow = new CompareFilesWindow(_fileSystemService)
        {
            Owner = this
        };
        compareWindow.CompareFiles(selection.LeftPath, selection.RightPath);
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

        if (TabReorderPlanner.TryCreateMove(sourceIndex, targetIndex, out var move))
        {
            _viewModel.Tabs.Move(move.SourceIndex, move.TargetIndex);
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
