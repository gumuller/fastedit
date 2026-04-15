namespace FastEdit.Services.Interfaces;

public interface IDialogService
{
    /// <summary>Show a message box and return the result.</summary>
    DialogResult ShowMessage(string message, string title, DialogButtons buttons = DialogButtons.Ok, DialogIcon icon = DialogIcon.Information);

    /// <summary>Show an open file dialog. Returns selected file path or null if cancelled.</summary>
    string? ShowOpenFileDialog(string? filter = null, string? initialDirectory = null);

    /// <summary>Show a save file dialog. Returns selected file path or null if cancelled.</summary>
    string? ShowSaveFileDialog(string? filter = null, string? defaultFileName = null, string? initialDirectory = null);

    /// <summary>Show a folder browser dialog. Returns selected folder path or null if cancelled.</summary>
    string? ShowFolderBrowserDialog(string? initialDirectory = null);

    /// <summary>Show multiple file open dialog. Returns selected file paths or empty array if cancelled.</summary>
    string[] ShowOpenFilesDialog(string? filter = null, string? initialDirectory = null);
}

public enum DialogResult
{
    None,
    Ok,
    Cancel,
    Yes,
    No
}

public enum DialogButtons
{
    Ok,
    OkCancel,
    YesNo,
    YesNoCancel
}

public enum DialogIcon
{
    None,
    Information,
    Warning,
    Error,
    Question
}
