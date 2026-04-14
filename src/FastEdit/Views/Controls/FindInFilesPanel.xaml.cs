using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FastEdit.ViewModels;

namespace FastEdit.Views.Controls;

public partial class FindInFilesPanel : UserControl
{
    public FindInFilesPanel()
    {
        InitializeComponent();
    }

    private void ClosePanel_Click(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is FindInFilesViewModel vm)
        {
            vm.SearchCommand.Execute(null);
        }
    }

    private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is SearchResult result)
        {
            if (DataContext is FindInFilesViewModel vm)
            {
                vm.NavigateToCommand.Execute(result);
            }
        }
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }
}
