using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace FastEdit.Theming;

public class ThemeLoader
{
    private readonly List<ThemeDefinition> _themes = new();
    private readonly string _customThemesPath;

    // Built-in themes discovered from embedded resources — used to distinguish
    // them from custom themes during refresh.
    private readonly HashSet<string> _builtInNames = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ThemeDefinition> Themes => _themes.AsReadOnly();

    public ThemeLoader()
    {
        _customThemesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit", "Themes");

        LoadBuiltInThemes();
        LoadCustomThemes();
    }

    private void LoadBuiltInThemes()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".json") && n.Contains("BuiltIn"));

        foreach (var resourceName in resourceNames)
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var theme = JsonSerializer.Deserialize<ThemeDefinition>(json);

                if (theme != null)
                {
                    _themes.Add(theme);
                    _builtInNames.Add(theme.Name);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to load built-in theme '{0}': {1}", resourceName, ex.Message);
            }
        }

        // Sort by preferred display order; unknown themes sort alphabetically at end
        var orderedNames = new[] { "Light", "Dark", "Nord", "RetroGreen", "SolarizedLight", "SolarizedDark", "Monokai", "Dracula", "OneDark" };
        _themes.Sort((a, b) =>
        {
            var indexA = Array.FindIndex(orderedNames, n => n.Equals(a.Name, StringComparison.OrdinalIgnoreCase));
            var indexB = Array.FindIndex(orderedNames, n => n.Equals(b.Name, StringComparison.OrdinalIgnoreCase));
            if (indexA == -1 && indexB == -1)
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            if (indexA == -1) return 1;
            if (indexB == -1) return -1;
            return indexA.CompareTo(indexB);
        });
    }

    public void LoadCustomThemes()
    {
        if (!Directory.Exists(_customThemesPath))
            return;

        var jsonFiles = Directory.GetFiles(_customThemesPath, "*.json");

        foreach (var file in jsonFiles)
        {
            try
            {
                var json = File.ReadAllText(file);
                var theme = JsonSerializer.Deserialize<ThemeDefinition>(json);

                if (theme != null)
                {
                    // Case-insensitive duplicate check
                    if (!_themes.Any(t => t.Name.Equals(theme.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        _themes.Add(theme);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to load custom theme '{0}': {1}", file, ex.Message);
            }
        }
    }

    public ThemeDefinition? GetTheme(string name)
    {
        return _themes.FirstOrDefault(t =>
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public void RefreshCustomThemes()
    {
        // Remove all non-built-in themes, then reload
        _themes.RemoveAll(t => !_builtInNames.Contains(t.Name));
        LoadCustomThemes();
    }

    public static ThemeDefinition? LoadFromFile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<ThemeDefinition>(json);
        }
        catch
        {
            return null;
        }
    }
}
