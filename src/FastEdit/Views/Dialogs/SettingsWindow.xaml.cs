using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FastEdit.Infrastructure;
using FastEdit.Services.Interfaces;
using FastEdit.Theming;
using FastEdit.ViewModels;

namespace FastEdit.Views.Dialogs;

public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainViewModel _viewModel;
    private readonly ISettingsService _settingsService;
    private readonly IKeyBindingService _keyBindingService;
    private readonly IDialogService _dialogService;
    private readonly ObservableCollection<KeyBindingEntry> _shortcutEntries = new();
    private KeyBindingEntry? _recordingEntry;

    public bool ShortcutsChanged { get; private set; }

    public SettingsWindow(
        MainViewModel viewModel,
        ISettingsService settingsService,
        IKeyBindingService keyBindingService,
        IDialogService dialogService)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _keyBindingService = keyBindingService ?? throw new ArgumentNullException(nameof(keyBindingService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        InitializeComponent();

        LoadCurrentValues();
        LoadShortcuts();
    }

    private void LoadCurrentValues()
    {
        // Editor
        FontSizeBox.Text = _viewModel.EditorFontSize.ToString("0");
        WordWrapCheck.IsChecked = _viewModel.IsWordWrapEnabled;
        ShowWhitespaceCheck.IsChecked = _viewModel.IsWhitespaceVisible;
        IndentGuidesCheck.IsChecked = _viewModel.IsIndentGuidesEnabled;
        CodeFoldingCheck.IsChecked = _viewModel.IsFoldingEnabled;
        MinimapCheck.IsChecked = _viewModel.IsMinimapVisible;
        AutoReloadCheck.IsChecked = _viewModel.IsAutoReloadEnabled;

        // Tab size
        var tabSize = _settingsService.TabSize;
        foreach (ComboBoxItem item in TabSizeCombo.Items)
        {
            if (item.Tag?.ToString() == tabSize.ToString())
            {
                TabSizeCombo.SelectedItem = item;
                break;
            }
        }
        if (TabSizeCombo.SelectedItem == null)
            TabSizeCombo.SelectedIndex = 1; // default to 4

        UseTabsCheck.IsChecked = _settingsService.UseTabs;

        // Cursor style
        foreach (ComboBoxItem item in CursorStyleCombo.Items)
        {
            if (item.Tag?.ToString() == _settingsService.CursorStyle)
            {
                CursorStyleCombo.SelectedItem = item;
                break;
            }
        }
        if (CursorStyleCombo.SelectedItem == null)
            CursorStyleCombo.SelectedIndex = 0; // default to Line

        // Appearance
        ThemeCombo.ItemsSource = _viewModel.AvailableThemes;
        var currentTheme = _viewModel.AvailableThemes.FirstOrDefault(
            t => t.Name == _viewModel.CurrentThemeName);
        ThemeCombo.SelectedItem = currentTheme;

        // General
        CheckUpdatesCheck.IsChecked = _settingsService.CheckForUpdatesOnStartup;
        AutoSaveBox.Text = _settingsService.AutoSaveIntervalSeconds.ToString();
    }

    private void LoadShortcuts()
    {
        _shortcutEntries.Clear();
        var bindings = _keyBindingService.GetBindings();
        foreach (var kvp in bindings.OrderBy(k => k.Key))
        {
            _shortcutEntries.Add(new KeyBindingEntry { Command = kvp.Key, Gesture = kvp.Value });
        }
        ShortcutsGrid.ItemsSource = _shortcutEntries;
    }

    private void FontSizeDown_Click(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(FontSizeBox.Text, out var size))
            FontSizeBox.Text = Math.Max(8, size - 1).ToString("0");
    }

    private void FontSizeUp_Click(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(FontSizeBox.Text, out var size))
            FontSizeBox.Text = Math.Min(72, size + 1).ToString("0");
    }

    private void RecordShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (ShortcutsGrid.SelectedItem is not KeyBindingEntry entry)
        {
            _dialogService.ShowMessage("Select a command first.", "Record Shortcut",
                DialogButtons.Ok, DialogIcon.Information);
            return;
        }

        _recordingEntry = entry;
        entry.Gesture = "Press key combination...";
        Title = "Settings — Recording shortcut...";
        PreviewKeyDown += OnRecordKeyDown;
    }

    private void OnRecordKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!ShortcutGestureFormatter.TryFormat(key, Keyboard.Modifiers, out var gesture))
            return;

        if (_recordingEntry != null)
        {
            _recordingEntry.Gesture = gesture;
            ShortcutsGrid.Items.Refresh();
        }

        _recordingEntry = null;
        Title = "Settings";
        PreviewKeyDown -= OnRecordKeyDown;
    }

    private void ResetShortcuts_Click(object sender, RoutedEventArgs e)
    {
        _keyBindingService.ResetToDefaults();
        LoadShortcuts();
        ShortcutsChanged = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Editor settings
        if (double.TryParse(FontSizeBox.Text, out var fontSize))
        {
            _viewModel.EditorFontSize = Math.Clamp(fontSize, 8, 72);
            _settingsService.EditorFontSize = _viewModel.EditorFontSize;
        }

        _viewModel.IsWordWrapEnabled = WordWrapCheck.IsChecked == true;
        _viewModel.IsWhitespaceVisible = ShowWhitespaceCheck.IsChecked == true;
        _viewModel.IsIndentGuidesEnabled = IndentGuidesCheck.IsChecked == true;
        _viewModel.IsFoldingEnabled = CodeFoldingCheck.IsChecked == true;
        _viewModel.IsMinimapVisible = MinimapCheck.IsChecked == true;
        _viewModel.IsAutoReloadEnabled = AutoReloadCheck.IsChecked == true;

        // Tab size
        if (TabSizeCombo.SelectedItem is ComboBoxItem tabItem &&
            int.TryParse(tabItem.Tag?.ToString(), out var tabSize))
        {
            _settingsService.TabSize = tabSize;
        }
        _settingsService.UseTabs = UseTabsCheck.IsChecked == true;

        // Cursor style
        if (CursorStyleCombo.SelectedItem is ComboBoxItem cursorItem)
        {
            _settingsService.CursorStyle = cursorItem.Tag?.ToString() ?? "Line";
        }

        // Theme
        if (ThemeCombo.SelectedItem is ThemeDefinition theme)
        {
            _viewModel.ChangeThemeCommand.Execute(theme.Name);
        }

        // General
        _settingsService.CheckForUpdatesOnStartup = CheckUpdatesCheck.IsChecked == true;
        if (int.TryParse(AutoSaveBox.Text, out var autoSave) && autoSave > 0)
        {
            _settingsService.AutoSaveIntervalSeconds = autoSave;
        }

        // Save keyboard shortcuts
        foreach (var entry in _shortcutEntries)
        {
            _keyBindingService.SetBinding(entry.Command, entry.Gesture);
        }
        ShortcutsChanged = true;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public class KeyBindingEntry
    {
        public string Command { get; set; } = string.Empty;
        public string Gesture { get; set; } = string.Empty;
    }
}
