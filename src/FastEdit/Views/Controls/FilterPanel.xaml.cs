using System.Windows;
using System.Windows.Controls;
using FastEdit.Models;
using FastEdit.Services.Interfaces;
using FastEdit.Views.Dialogs;

namespace FastEdit.Views.Controls;

public partial class FilterPanel : UserControl
{
    private const string FilterFileDialogFilter = "Filter Files (*.filters)|*.filters|All Files (*.*)|*.*";

    private ILineFilterService? _filterService;
    private IDialogService? _dialogService;
    private bool _isSubscribedToFilterService;

    public event Action? CloseRequested;
    public event Action? FiltersUpdated;
    public event Action? NavigateNextRequested;
    public event Action? NavigatePrevRequested;

    public FilterPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void SetServices(ILineFilterService filterService, IDialogService dialogService)
    {
        if (ReferenceEquals(_filterService, filterService))
        {
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            return;
        }

        UnsubscribeFromFilterService();
        _filterService = filterService ?? throw new ArgumentNullException(nameof(filterService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        FilterList.ItemsSource = _filterService.Filters;
        SubscribeToFilterService();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_filterService == null || _dialogService == null)
            throw new InvalidOperationException("FilterPanel requires ILineFilterService and IDialogService before it is loaded.");

        SubscribeToFilterService();
    }

    private ILineFilterService RequireFilterService() =>
        _filterService ?? throw new InvalidOperationException("FilterPanel requires ILineFilterService before use.");

    private IDialogService RequireDialogService() =>
        _dialogService ?? throw new InvalidOperationException("FilterPanel requires IDialogService before use.");

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnsubscribeFromFilterService();
    }

    private void SubscribeToFilterService()
    {
        if (_filterService == null || _isSubscribedToFilterService)
            return;

        _filterService.FiltersChanged += OnFiltersChanged;
        _isSubscribedToFilterService = true;
    }

    private void UnsubscribeFromFilterService()
    {
        if (_filterService == null || !_isSubscribedToFilterService)
            return;

        _filterService.FiltersChanged -= OnFiltersChanged;
        _isSubscribedToFilterService = false;
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
        var dialogService = RequireDialogService();
        var filterService = RequireFilterService();

        var dialog = new FilterEditDialog(dialogService) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            filterService.AddFilter(dialog.Result);
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
        if (FilterList.SelectedItem is not LineFilter selected) return;

        var dialogService = RequireDialogService();

        var dialog = new FilterEditDialog(dialogService, selected) { Owner = Window.GetWindow(this) };
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
        var dialogService = RequireDialogService();
        var filterService = RequireFilterService();

        var fileName = dialogService.ShowSaveFileDialog(FilterFileDialogFilter, "filters.filters");
        if (fileName != null)
        {
            try
            {
                filterService.SaveFilters(fileName);
            }
            catch (Exception ex)
            {
                dialogService.ShowMessage($"Failed to save filters: {ex.Message}", "Error",
                    DialogButtons.Ok, DialogIcon.Error);
            }
        }
    }

    private void LoadFilters_Click(object sender, RoutedEventArgs e)
    {
        var dialogService = RequireDialogService();
        var filterService = RequireFilterService();

        var fileName = dialogService.ShowOpenFileDialog(FilterFileDialogFilter);
        if (fileName != null)
        {
            try
            {
                filterService.LoadFilters(fileName);
            }
            catch (Exception ex)
            {
                dialogService.ShowMessage($"Failed to load filters: {ex.Message}", "Error",
                    DialogButtons.Ok, DialogIcon.Error);
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
