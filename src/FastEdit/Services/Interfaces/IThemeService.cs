using FastEdit.Theming;

namespace FastEdit.Services.Interfaces;

public interface IThemeService
{
    IReadOnlyList<ThemeDefinition> AvailableThemes { get; }
    ThemeDefinition CurrentTheme { get; }
    event EventHandler<ThemeDefinition>? ThemeChanged;

    void ApplyTheme(string themeName);
    void RefreshCustomThemes();
}
