using System.Windows;
using System.Windows.Media;
using FastEdit.Services.Interfaces;
using FastEdit.Theming;

namespace FastEdit.Services;

public class ThemeService : IThemeService
{
    private readonly ThemeLoader _themeLoader;

    public IReadOnlyList<ThemeDefinition> AvailableThemes => _themeLoader.Themes;
    public ThemeDefinition CurrentTheme { get; private set; } = null!;

    public event EventHandler<ThemeDefinition>? ThemeChanged;

    public ThemeService()
    {
        _themeLoader = new ThemeLoader();

        // Set default theme
        var defaultTheme = _themeLoader.GetTheme("Dark") ?? _themeLoader.Themes.FirstOrDefault();
        if (defaultTheme != null)
        {
            CurrentTheme = defaultTheme;
        }
    }

    public void ApplyTheme(string themeName)
    {
        var theme = _themeLoader.GetTheme(themeName);
        if (theme == null) return;

        CurrentTheme = theme;
        ApplyWpfTheme(theme);
        ThemeChanged?.Invoke(this, theme);
    }

    private void ApplyWpfTheme(ThemeDefinition theme)
    {
        var resources = Application.Current.Resources;
        var colors = theme.Colors;

        // Window
        resources["WindowBackgroundBrush"] = CreateBrush(colors.WindowBackground);
        resources["WindowForegroundBrush"] = CreateBrush(colors.WindowForeground);
        resources["TitleBarBackgroundBrush"] = CreateBrush(colors.TitleBarBackground);
        resources["TitleBarForegroundBrush"] = CreateBrush(colors.TitleBarForeground);

        // Editor
        resources["EditorBackgroundBrush"] = CreateBrush(colors.EditorBackground);
        resources["EditorForegroundBrush"] = CreateBrush(colors.EditorForeground);
        resources["EditorLineNumbersForegroundBrush"] = CreateBrush(colors.EditorLineNumbersForeground);
        resources["EditorCurrentLineBackgroundBrush"] = CreateBrush(colors.EditorCurrentLineBackground);
        resources["EditorSelectionBackgroundBrush"] = CreateBrush(colors.EditorSelectionBackground);
        resources["EditorSelectionForegroundBrush"] = CreateBrush(colors.EditorSelectionForeground);

        // Hex Editor
        resources["HexOffsetForegroundBrush"] = CreateBrush(colors.HexOffsetForeground);
        resources["HexBytesForegroundBrush"] = CreateBrush(colors.HexBytesForeground);
        resources["HexAsciiForegroundBrush"] = CreateBrush(colors.HexAsciiForeground);
        resources["HexModifiedBackgroundBrush"] = CreateBrush(colors.HexModifiedBackground);
        resources["HexNullByteForegroundBrush"] = CreateBrush(colors.HexNullByteForeground);

        // Panels
        resources["PanelBackgroundBrush"] = CreateBrush(colors.PanelBackground);
        resources["PanelBorderBrush"] = CreateBrush(colors.PanelBorder);
        resources["TreeViewBackgroundBrush"] = CreateBrush(colors.TreeViewBackground);
        resources["TreeViewItemHoverBrush"] = CreateBrush(colors.TreeViewItemHover);
        resources["TreeViewItemSelectedBrush"] = CreateBrush(colors.TreeViewItemSelected);

        // Tabs
        resources["TabBackgroundBrush"] = CreateBrush(colors.TabBackground);
        resources["TabActiveBackgroundBrush"] = CreateBrush(colors.TabActiveBackground);
        resources["TabForegroundBrush"] = CreateBrush(colors.TabForeground);
        resources["TabActiveForegroundBrush"] = CreateBrush(colors.TabActiveForeground);
        resources["TabBorderBrush"] = CreateBrush(colors.TabBorder);

        // Status Bar
        resources["StatusBarBackgroundBrush"] = CreateBrush(colors.StatusBarBackground);
        resources["StatusBarForegroundBrush"] = CreateBrush(colors.StatusBarForeground);

        // Buttons
        resources["ButtonBackgroundBrush"] = CreateBrush(colors.ButtonBackground);
        resources["ButtonForegroundBrush"] = CreateBrush(colors.ButtonForeground);
        resources["ButtonHoverBackgroundBrush"] = CreateBrush(colors.ButtonHoverBackground);

        // Scrollbars
        resources["ScrollBarBackgroundBrush"] = CreateBrush(colors.ScrollBarBackground);
        resources["ScrollBarThumbBrush"] = CreateBrush(colors.ScrollBarThumb);

        // Accents
        resources["AccentBrush"] = CreateBrush(colors.AccentColor);
        resources["ErrorBrush"] = CreateBrush(colors.ErrorColor);
        resources["WarningBrush"] = CreateBrush(colors.WarningColor);
        resources["SuccessBrush"] = CreateBrush(colors.SuccessColor);

        // Menu colors (derived from panel colors)
        resources["MenuBackgroundBrush"] = CreateBrush(colors.PanelBackground);
        resources["MenuForegroundBrush"] = CreateBrush(colors.WindowForeground);
        resources["MenuHoverBackgroundBrush"] = CreateBrush(colors.TreeViewItemHover);
        resources["MenuBorderBrush"] = CreateBrush(colors.PanelBorder);
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public void RefreshCustomThemes()
    {
        _themeLoader.RefreshCustomThemes();
    }
}
