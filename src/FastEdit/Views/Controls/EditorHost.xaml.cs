using System.Windows;
using System.Windows.Controls;
using FastEdit.ViewModels;
using ICSharpCode.AvalonEdit.Highlighting;

namespace FastEdit.Views.Controls;

public partial class EditorHost : UserControl
{
    private EditorTabViewModel? _currentVm;

    public EditorHost()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Ensure editor is properly initialized when loaded
        if (_currentVm != null)
        {
            UpdateEditor(_currentVm);
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is EditorTabViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _currentVm = e.NewValue as EditorTabViewModel;

        if (_currentVm != null)
        {
            _currentVm.PropertyChanged += OnViewModelPropertyChanged;
            UpdateEditor(_currentVm);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not EditorTabViewModel vm) return;

        if (e.PropertyName == nameof(EditorTabViewModel.Content))
        {
            if (!vm.IsBinaryMode && TextEditor.Text != vm.Content)
            {
                TextEditor.Text = vm.Content;
            }
        }
        else if (e.PropertyName == nameof(EditorTabViewModel.IsBinaryMode))
        {
            UpdateEditor(vm);
        }
    }

    private void UpdateEditor(EditorTabViewModel vm)
    {
        // Explicitly set visibility
        if (vm.IsBinaryMode)
        {
            TextEditor.Visibility = Visibility.Collapsed;
            HexEditor.Visibility = Visibility.Visible;
            HexEditor.DataContext = vm;

            // Bring hex editor to front
            Panel.SetZIndex(HexEditor, 1);
            Panel.SetZIndex(TextEditor, 0);
        }
        else
        {
            HexEditor.Visibility = Visibility.Collapsed;
            TextEditor.Visibility = Visibility.Visible;

            // Bring text editor to front
            Panel.SetZIndex(TextEditor, 1);
            Panel.SetZIndex(HexEditor, 0);

            // Setup text editor
            TextEditor.Text = vm.Content;

            // Remove old handler to prevent duplicates
            TextEditor.TextChanged -= TextEditor_TextChanged;
            TextEditor.TextChanged += TextEditor_TextChanged;

            // Apply syntax highlighting
            var highlighting = GetHighlightingForLanguage(vm.SyntaxLanguage);
            TextEditor.SyntaxHighlighting = highlighting;

            // Track caret position - remove old handler first
            TextEditor.TextArea.Caret.PositionChanged -= Caret_PositionChanged;
            TextEditor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
        }
    }

    private void TextEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_currentVm != null && !_currentVm.IsBinaryMode)
        {
            _currentVm.Content = TextEditor.Text;
        }
    }

    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        if (_currentVm != null)
        {
            _currentVm.Line = TextEditor.TextArea.Caret.Line;
            _currentVm.Column = TextEditor.TextArea.Caret.Column;
        }
    }

    private static IHighlightingDefinition? GetHighlightingForLanguage(string language)
    {
        if (string.IsNullOrEmpty(language))
            return null;

        return language switch
        {
            "C#" => HighlightingManager.Instance.GetDefinition("C#"),
            "JavaScript" => HighlightingManager.Instance.GetDefinition("JavaScript"),
            "TypeScript" => HighlightingManager.Instance.GetDefinition("JavaScript"),
            "Python" => HighlightingManager.Instance.GetDefinition("Python"),
            "Java" => HighlightingManager.Instance.GetDefinition("Java"),
            "C++" => HighlightingManager.Instance.GetDefinition("C++"),
            "C" => HighlightingManager.Instance.GetDefinition("C++"),
            "HTML" => HighlightingManager.Instance.GetDefinition("HTML"),
            "CSS" => HighlightingManager.Instance.GetDefinition("CSS"),
            "XML" => HighlightingManager.Instance.GetDefinition("XML"),
            "JSON" => HighlightingManager.Instance.GetDefinition("Json"),
            "SQL" => HighlightingManager.Instance.GetDefinition("TSQL"),
            "PowerShell" => HighlightingManager.Instance.GetDefinition("PowerShell"),
            "Markdown" => HighlightingManager.Instance.GetDefinition("MarkDown"),
            _ => null
        };
    }
}
