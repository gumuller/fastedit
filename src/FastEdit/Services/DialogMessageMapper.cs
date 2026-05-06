using System.Windows;
using FastEdit.Services.Interfaces;
using DialogResult = FastEdit.Services.Interfaces.DialogResult;

namespace FastEdit.Services;

public static class DialogMessageMapper
{
    private static readonly IReadOnlyDictionary<DialogButtons, MessageBoxButton> ButtonMap =
        new Dictionary<DialogButtons, MessageBoxButton>
        {
            [DialogButtons.Ok] = MessageBoxButton.OK,
            [DialogButtons.OkCancel] = MessageBoxButton.OKCancel,
            [DialogButtons.YesNo] = MessageBoxButton.YesNo,
            [DialogButtons.YesNoCancel] = MessageBoxButton.YesNoCancel,
        };

    private static readonly IReadOnlyDictionary<DialogIcon, MessageBoxImage> IconMap =
        new Dictionary<DialogIcon, MessageBoxImage>
        {
            [DialogIcon.Information] = MessageBoxImage.Information,
            [DialogIcon.Warning] = MessageBoxImage.Warning,
            [DialogIcon.Error] = MessageBoxImage.Error,
            [DialogIcon.Question] = MessageBoxImage.Question,
        };

    private static readonly IReadOnlyDictionary<MessageBoxResult, DialogResult> ResultMap =
        new Dictionary<MessageBoxResult, DialogResult>
        {
            [MessageBoxResult.OK] = DialogResult.Ok,
            [MessageBoxResult.Cancel] = DialogResult.Cancel,
            [MessageBoxResult.Yes] = DialogResult.Yes,
            [MessageBoxResult.No] = DialogResult.No,
        };

    public static MessageBoxButton ToMessageBoxButton(DialogButtons buttons) =>
        ButtonMap.GetValueOrDefault(buttons, MessageBoxButton.OK);

    public static MessageBoxImage ToMessageBoxImage(DialogIcon icon) =>
        IconMap.GetValueOrDefault(icon, MessageBoxImage.None);

    public static DialogResult ToDialogResult(MessageBoxResult result) =>
        ResultMap.GetValueOrDefault(result, DialogResult.None);
}
