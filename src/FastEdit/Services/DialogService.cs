using System.Windows;
using FastEdit.Services.Interfaces;
using Microsoft.Win32;
using DialogResult = FastEdit.Services.Interfaces.DialogResult;

namespace FastEdit.Services;

public class DialogService : IDialogService
{
    public DialogResult ShowMessage(string message, string title, DialogButtons buttons = DialogButtons.Ok, DialogIcon icon = DialogIcon.Information)
    {
        var result = MessageBox.Show(
            message,
            title,
            DialogMessageMapper.ToMessageBoxButton(buttons),
            DialogMessageMapper.ToMessageBoxImage(icon));
        return DialogMessageMapper.ToDialogResult(result);
    }

    public string? ShowOpenFileDialog(string? filter = null, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog();
        if (filter != null) dialog.Filter = filter;
        if (initialDirectory != null) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowSaveFileDialog(string? filter = null, string? defaultFileName = null, string? initialDirectory = null)
    {
        var dialog = new SaveFileDialog();
        if (filter != null) dialog.Filter = filter;
        if (defaultFileName != null) dialog.FileName = defaultFileName;
        if (initialDirectory != null) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowFolderBrowserDialog(string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog();
        if (initialDirectory != null) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string[] ShowOpenFilesDialog(string? filter = null, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog { Multiselect = true };
        if (filter != null) dialog.Filter = filter;
        if (initialDirectory != null) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
    }

    public string? ShowInputDialog(string title, string prompt, string? defaultValue = null)
    {
        // Simple WPF input dialog
        var win = new Window
        {
            Title = title,
            Width = 400,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = Application.Current.MainWindow
        };

        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(15) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
        var textBox = new System.Windows.Controls.TextBox { Text = defaultValue ?? "" };
        panel.Children.Add(textBox);

        var buttonPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var okButton = new System.Windows.Controls.Button { Content = "OK", Width = 75, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancelButton = new System.Windows.Controls.Button { Content = "Cancel", Width = 75, IsCancel = true };

        okButton.Click += (s, e) => { win.DialogResult = true; win.Close(); };
        cancelButton.Click += (s, e) => { win.DialogResult = false; win.Close(); };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        panel.Children.Add(buttonPanel);
        win.Content = panel;

        textBox.SelectAll();
        textBox.Focus();

        return win.ShowDialog() == true ? textBox.Text : null;
    }
}
