using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace FastEdit.Helpers;

public static class FormatHelper
{
    public static (string result, string? error) PrettyPrintJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var options = new JsonWriterOptions { Indented = true };
            using var stream = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, options))
            {
                doc.WriteTo(writer);
            }
            return (System.Text.Encoding.UTF8.GetString(stream.ToArray()), null);
        }
        catch (JsonException ex)
        {
            return (text, $"JSON error: {ex.Message}");
        }
    }

    public static (string result, string? error) MinifyJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var options = new JsonWriterOptions { Indented = false };
            using var stream = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, options))
            {
                doc.WriteTo(writer);
            }
            return (System.Text.Encoding.UTF8.GetString(stream.ToArray()), null);
        }
        catch (JsonException ex)
        {
            return (text, $"JSON error: {ex.Message}");
        }
    }

    public static (string result, string? error) PrettyPrintXml(string text)
    {
        try
        {
            var xdoc = XDocument.Parse(text);
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = xdoc.Declaration == null
            };
            using var sw = new System.IO.StringWriter();
            using (var writer = XmlWriter.Create(sw, settings))
            {
                xdoc.WriteTo(writer);
            }
            return (sw.ToString(), null);
        }
        catch (XmlException ex)
        {
            return (text, $"XML error: {ex.Message}");
        }
    }

    public static (string result, string? error) MinifyXml(string text)
    {
        try
        {
            var xdoc = XDocument.Parse(text, LoadOptions.None);
            var settings = new XmlWriterSettings
            {
                Indent = false,
                OmitXmlDeclaration = xdoc.Declaration == null
            };
            using var sw = new System.IO.StringWriter();
            using (var writer = XmlWriter.Create(sw, settings))
            {
                xdoc.WriteTo(writer);
            }
            return (sw.ToString(), null);
        }
        catch (XmlException ex)
        {
            return (text, $"XML error: {ex.Message}");
        }
    }

    public static bool IsJsonLanguage(string language) =>
        language.Equals("JSON", StringComparison.OrdinalIgnoreCase);

    public static bool IsXmlLanguage(string language) =>
        language.Equals("XML", StringComparison.OrdinalIgnoreCase) ||
        language.Equals("HTML", StringComparison.OrdinalIgnoreCase);

    public static bool IsFormattable(string language) =>
        IsJsonLanguage(language) || IsXmlLanguage(language);
}
