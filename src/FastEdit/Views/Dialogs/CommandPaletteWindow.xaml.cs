using System.Windows;
using System.Windows.Input;
using FastEdit.Infrastructure;

namespace FastEdit.Views.Dialogs;

public partial class CommandPaletteWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly CommandRegistry _registry;

    public CommandDescriptor? SelectedCommand { get; private set; }

    public CommandPaletteWindow(CommandRegistry registry)
    {
        _registry = registry;
        InitializeComponent();

        CommandList.ItemsSource = registry.Commands;
        Loaded += (s, e) =>
        {
            SearchBox.Focus();
        };
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var results = _registry.Search(SearchBox.Text).ToList();
        CommandList.ItemsSource = results;
        if (results.Count > 0)
            CommandList.SelectedIndex = 0;
    }

    private void ExecuteSelected()
    {
        if (CommandList.SelectedItem is CommandDescriptor cmd)
        {
            SelectedCommand = cmd;
            DialogResult = true;
            Close();
        }
    }

    private void CommandList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteSelected();
    }

    private void CommandList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteSelected();
            e.Handled = true;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && SearchBox.IsFocused)
        {
            CommandList.Focus();
            if (CommandList.Items.Count > 0)
                CommandList.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && SearchBox.IsFocused)
        {
            ExecuteSelected();
            e.Handled = true;
        }
    }
}
