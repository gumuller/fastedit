using System.Windows;
using FastEdit.Services;
using FastEdit.Services.Interfaces;
using DialogResult = FastEdit.Services.Interfaces.DialogResult;

namespace FastEdit.Tests;

public class DialogMessageMapperTests
{
    [Theory]
    [InlineData(DialogButtons.Ok, MessageBoxButton.OK)]
    [InlineData(DialogButtons.OkCancel, MessageBoxButton.OKCancel)]
    [InlineData(DialogButtons.YesNo, MessageBoxButton.YesNo)]
    [InlineData(DialogButtons.YesNoCancel, MessageBoxButton.YesNoCancel)]
    public void ToMessageBoxButton_MapsDialogButtons(DialogButtons buttons, MessageBoxButton expected)
    {
        Assert.Equal(expected, DialogMessageMapper.ToMessageBoxButton(buttons));
    }

    [Theory]
    [InlineData(DialogIcon.Information, MessageBoxImage.Information)]
    [InlineData(DialogIcon.Warning, MessageBoxImage.Warning)]
    [InlineData(DialogIcon.Error, MessageBoxImage.Error)]
    [InlineData(DialogIcon.Question, MessageBoxImage.Question)]
    public void ToMessageBoxImage_MapsDialogIcons(DialogIcon icon, MessageBoxImage expected)
    {
        Assert.Equal(expected, DialogMessageMapper.ToMessageBoxImage(icon));
    }

    [Theory]
    [InlineData(MessageBoxResult.OK, DialogResult.Ok)]
    [InlineData(MessageBoxResult.Cancel, DialogResult.Cancel)]
    [InlineData(MessageBoxResult.Yes, DialogResult.Yes)]
    [InlineData(MessageBoxResult.No, DialogResult.No)]
    public void ToDialogResult_MapsMessageBoxResults(MessageBoxResult result, DialogResult expected)
    {
        Assert.Equal(expected, DialogMessageMapper.ToDialogResult(result));
    }
}
