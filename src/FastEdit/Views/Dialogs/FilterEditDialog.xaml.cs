using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FastEdit.Infrastructure;
using FastEdit.Models;
using FastEdit.Services.Interfaces;
using Wpf.Ui.Controls;

namespace FastEdit.Views.Dialogs;

public partial class FilterEditDialog : FluentWindow
{
    private static readonly string[] PaletteColors =
    {
        "#FF4444", "#FF8800", "#FFCC00", "#44BB44", "#44CCCC",
        "#4488FF", "#8844FF", "#FF44AA", "#888888", "#CCCCCC"
    };

    private string _selectedColor = "#4488FF";
    private readonly IDialogService _dialogService;

    public LineFilter? Result { get; private set; }

    public FilterEditDialog(IDialogService dialogService, LineFilter? existing = null)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        InitializeComponent();
        BuildColorPalette();

        if (existing != null)
        {
            Title = "Edit Filter";
            PatternBox.Text = existing.Pattern;
            RegexCheck.IsChecked = existing.IsRegex;
            CaseCheck.IsChecked = existing.IsCaseSensitive;
            ExcludeCheck.IsChecked = existing.IsExcluding;
            _selectedColor = existing.BackgroundColor;
        }

        HighlightSelectedColor();
        Loaded += (_, _) => PatternBox.Focus();
    }

    private void BuildColorPalette()
    {
        foreach (var hex in PaletteColors)
        {
            var border = new Border
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = hex
            };
            border.MouseLeftButtonDown += (s, _) =>
            {
                _selectedColor = (string)((Border)s!).Tag;
                HighlightSelectedColor();
            };
            ColorPalette.Children.Add(border);
        }
    }

    private void HighlightSelectedColor()
    {
        foreach (Border b in ColorPalette.Children)
        {
            b.BorderBrush = (string)b.Tag == _selectedColor
                ? (Brush)FindResource("AccentBrush")
                : Brushes.Transparent;
        }
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        var validation = LineFilterInputValidator.Validate(PatternBox.Text, RegexCheck.IsChecked == true);
        if (!validation.IsValid)
        {
            _dialogService.ShowMessage(
                validation.ErrorMessage ?? "Invalid filter.",
                "Validation",
                DialogButtons.Ok,
                DialogIcon.Warning);
            return;
        }

        Result = new LineFilter
        {
            Pattern = validation.Pattern,
            IsRegex = RegexCheck.IsChecked == true,
            IsCaseSensitive = CaseCheck.IsChecked == true,
            IsExcluding = ExcludeCheck.IsChecked == true,
            BackgroundColor = _selectedColor
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
