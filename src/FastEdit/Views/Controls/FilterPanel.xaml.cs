using System.Windows;
using System.Windows.Controls;
using FastEdit.Models;
using FastEdit.Services.Interfaces;
using FastEdit.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace FastEdit.Views.Controls;

public partial class FilterPanel : UserControl
{
    private ILineFilterService? _filterService;

    public event Action? CloseRequested;
    public event Action? FiltersUpdated;
    public event Action? NavigateNextRequested;
    public event Action? NavigatePrevRequested;

    public FilterPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _filterService = App.Services.GetRequiredService<ILineFilterService>();
        FilterList.ItemsSource = _filterService.Filters;
        _filterService.FiltersChanged += OnFiltersChanged;
    }

    private void OnFiltersChanged()
    {
        Dispatcher.Invoke(() =>
        {
            FiltersUpdated?.Invoke();
        });
    }

    public void UpdateMatchCount(int matched, int total)
    {
        MatchCountText.Text = matched > 0 || _filterService?.HasActiveFilters == true
            ? $"— {matched}/{total} lines match"
            : "";
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new FilterEditDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _filterService?.AddFilter(dialog.Result);
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        EditSelectedFilter();
    }

    private void FilterList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        EditSelectedFilter();
    }

    private void EditSelectedFilter()
    {
        if (FilterList.SelectedItem is not LineFilter selected || _filterService == null) return;

        var dialog = new FilterEditDialog(selected) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            var result = dialog.Result;
            selected.Pattern = result.Pattern;
            selected.IsRegex = result.IsRegex;
            selected.IsCaseSensitive = result.IsCaseSensitive;
            selected.IsExcluding = result.IsExcluding;
            selected.BackgroundColor = result.BackgroundColor;
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (FilterList.SelectedItem is LineFilter selected)
            _filterService?.RemoveFilter(selected);
    }

    private void EnableAll_Click(object sender, RoutedEventArgs e)
    {
        _filterService?.EnableAll();
    }

    private void DisableAll_Click(object sender, RoutedEventArgs e)
    {
        _filterService?.DisableAll();
    }

    private void ShowOnlyToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_filterService != null)
            _filterService.ShowOnlyFilteredLines = ShowOnlyToggle.IsChecked == true;
    }

    private void SaveFilters_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Filter Files (*.filters)|*.filters|All Files (*.*)|*.*",
            DefaultExt = ".filters",
            FileName = "filters.filters"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                _filterService?.SaveFilters(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save filters: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void LoadFilters_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Filter Files (*.filters)|*.filters|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                _filterService?.LoadFilters(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load filters: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
    }

    private void NextMatch_Click(object sender, RoutedEventArgs e)
    {
        NavigateNextRequested?.Invoke();
    }

    private void PrevMatch_Click(object sender, RoutedEventArgs e)
    {
        NavigatePrevRequested?.Invoke();
    }
}
