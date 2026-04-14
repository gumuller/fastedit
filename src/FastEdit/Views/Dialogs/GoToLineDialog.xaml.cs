using System.Windows;

namespace FastEdit.Views;

public partial class GoToLineDialog : Window
{
    public int LineNumber { get; private set; }

    public GoToLineDialog(int currentLine = 1)
    {
        InitializeComponent();
        LineNumberBox.Text = currentLine.ToString();
        LineNumberBox.SelectAll();
        Loaded += (_, _) => LineNumberBox.Focus();
    }

    private void Go_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(LineNumberBox.Text, out var line) && line > 0)
        {
            LineNumber = line;
            DialogResult = true;
        }
        else
        {
            System.Windows.MessageBox.Show("Please enter a valid line number.",
                "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
