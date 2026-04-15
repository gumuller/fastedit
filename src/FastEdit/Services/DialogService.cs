using System.Windows;
using FastEdit.Services.Interfaces;
using Microsoft.Win32;
using DialogResult = FastEdit.Services.Interfaces.DialogResult;

namespace FastEdit.Services;

public class DialogService : IDialogService
{
    public DialogResult ShowMessage(string message, string title, DialogButtons buttons = DialogButtons.Ok, DialogIcon icon = DialogIcon.Information)
    {
        var mbButton = buttons switch
        {
            DialogButtons.Ok => MessageBoxButton.OK,
            DialogButtons.OkCancel => MessageBoxButton.OKCancel,
            DialogButtons.YesNo => MessageBoxButton.YesNo,
            DialogButtons.YesNoCancel => MessageBoxButton.YesNoCancel,
            _ => MessageBoxButton.OK
        };

        var mbIcon = icon switch
        {
            DialogIcon.Information => MessageBoxImage.Information,
            DialogIcon.Warning => MessageBoxImage.Warning,
            DialogIcon.Error => MessageBoxImage.Error,
            DialogIcon.Question => MessageBoxImage.Question,
            _ => MessageBoxImage.None
        };

        var result = MessageBox.Show(message, title, mbButton, mbIcon);
        return result switch
        {
            MessageBoxResult.OK => DialogResult.Ok,
            MessageBoxResult.Cancel => DialogResult.Cancel,
            MessageBoxResult.Yes => DialogResult.Yes,
            MessageBoxResult.No => DialogResult.No,
            _ => DialogResult.None
        };
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
}
