using System.Reflection;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace FastEdit.Helpers;

/// <summary>
/// Registers custom .xshd syntax definitions with AvalonEdit's HighlightingManager.
/// </summary>
public static class SyntaxHighlightingRegistrar
{
    private static readonly string[] CustomDefinitions =
    [
        "YAML", "Bash", "Dockerfile", "Rust", "Go", "TOML", "INI"
    ];

    public static void RegisterAll()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var manager = HighlightingManager.Instance;

        foreach (var name in CustomDefinitions)
        {
            var resourceName = $"FastEdit.SyntaxHighlighting.{name}.xshd";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;

            using var reader = new XmlTextReader(stream);
            var definition = HighlightingLoader.Load(reader, manager);

            // Register with the name from the .xshd file
            var extensions = definition.Properties.ContainsKey("Extensions")
                ? definition.Properties["Extensions"].Split(';')
                : [];

            manager.RegisterHighlighting(definition.Name, extensions, definition);
        }
    }
}
