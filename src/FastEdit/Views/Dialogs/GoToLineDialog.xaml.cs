using System.Windows;
using FastEdit.Infrastructure;
using FastEdit.Services.Interfaces;

namespace FastEdit.Views;

public partial class GoToLineDialog : Window
{
    private readonly IDialogService _dialogService;

    public int LineNumber { get; private set; }

    public GoToLineDialog(int currentLine, IDialogService dialogService)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        InitializeComponent();
        LineNumberBox.Text = currentLine.ToString();
        LineNumberBox.SelectAll();
        Loaded += (_, _) => LineNumberBox.Focus();
    }

    private void Go_Click(object sender, RoutedEventArgs e)
    {
        if (GoToLineInputParser.TryParse(LineNumberBox.Text, out var line))
        {
            LineNumber = line;
            DialogResult = true;
        }
        else
        {
            _dialogService.ShowMessage(
                "Please enter a valid line number.",
                "Invalid Input",
                DialogButtons.Ok,
                DialogIcon.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
