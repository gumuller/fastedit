using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FastEdit.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FastEdit.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<MainViewModel>();
        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Restore previous session
        if (_viewModel != null)
        {
            await _viewModel.RestoreSessionAsync();
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_viewModel == null) return;

        // Confirm if there are unsaved changes
        if (!_viewModel.ConfirmExit())
        {
            e.Cancel = true;
            return;
        }

        // Save session before closing
        _viewModel.SaveSession();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "FastEdit v1.0\n\nA fast, lightweight text and hex editor for Windows.\n\nFeatures:\n- Syntax highlighting for 20+ languages\n- Hex editing for binary files\n- Theme support (Light, Dark, Nord, Retro Green)\n- Virtual scrolling for large files\n- Session restore",
            "About FastEdit",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void FileTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileTreeView.SelectedItem is FileNodeViewModel node && !node.IsDirectory)
        {
            _viewModel?.OpenFileCommand.Execute(node.FullPath);
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
