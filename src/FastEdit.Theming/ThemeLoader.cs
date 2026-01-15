using System.Reflection;
using System.Text.Json;

namespace FastEdit.Theming;

public class ThemeLoader
{
    private readonly List<ThemeDefinition> _themes = new();
    private readonly string _customThemesPath;

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
                }
            }
            catch (Exception)
            {
                // Skip invalid themes
            }
        }

        // Ensure we have themes in a predictable order
        var orderedNames = new[] { "Light", "Dark", "Nord", "RetroGreen" };
        _themes.Sort((a, b) =>
        {
            var indexA = Array.IndexOf(orderedNames, a.Name);
            var indexB = Array.IndexOf(orderedNames, b.Name);
            if (indexA == -1) indexA = int.MaxValue;
            if (indexB == -1) indexB = int.MaxValue;
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
                    // Don't add duplicates
                    if (!_themes.Any(t => t.Name == theme.Name))
                    {
                        _themes.Add(theme);
                    }
                }
            }
            catch (Exception)
            {
                // Skip invalid themes
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
        // Remove non-built-in themes
        _themes.RemoveAll(t =>
            t.Name != "Light" && t.Name != "Dark" &&
            t.Name != "Nord" && t.Name != "RetroGreen");

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
