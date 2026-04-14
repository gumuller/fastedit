using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FastEdit.ViewModels;
using FastEdit.Views.Controls;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;

namespace FastEdit.Views;

public partial class MainWindow : FluentWindow
{
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<MainViewModel>();
        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;

        // Subscribe to GoToLine event
        _viewModel.GoToLineRequested += OnGoToLineRequested;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.RestoreSessionAsync();
        }
    }

    private bool _isClosingConfirmed;

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_viewModel == null) return;

        if (_isClosingConfirmed)
        {
            _viewModel.SaveSession();
            return;
        }

        if (!_viewModel.HasUnsavedChanges())
        {
            _isClosingConfirmed = true;
            _viewModel.SaveSession();
            return;
        }

        e.Cancel = true;

        var canClose = await _viewModel.ConfirmExitAsync();
        if (canClose)
        {
            _isClosingConfirmed = true;
            _viewModel.SaveSession();
            Close();
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.MessageBox.Show(
            "FastEdit v1.0\n\nA fast, lightweight text and hex editor for Windows.\n\nFeatures:\n- Syntax highlighting for 20+ languages\n- Hex editing for binary files\n- 9 built-in themes\n- Virtual scrolling for large files\n- Session restore\n- Find & Replace\n- Recent files\n- Zoom, word wrap, whitespace\n- Line operations",
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
                }
            }
        }
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
}
