using FastEdit.Helpers;

namespace FastEdit.Tests;

public class FormatHelperTests
{
    // --- JSON Pretty Print ---
    [Fact]
    public void PrettyPrintJson_Formats_Compact_Json()
    {
        var input = """{"name":"John","age":30}""";
        var (result, error) = FormatHelper.PrettyPrintJson(input);

        Assert.Null(error);
        Assert.Contains("\"name\"", result);
        Assert.Contains("\n", result); // should be indented
        Assert.Contains("John", result);
    }

    [Fact]
    public void PrettyPrintJson_Preserves_Values()
    {
        var input = """{"items":[1,2,3],"flag":true,"val":null}""";
        var (result, error) = FormatHelper.PrettyPrintJson(input);

        Assert.Null(error);
        Assert.Contains("true", result);
        Assert.Contains("null", result);
    }

    [Fact]
    public void PrettyPrintJson_Returns_Error_For_Invalid_Json()
    {
        var (result, error) = FormatHelper.PrettyPrintJson("{invalid json}");

        Assert.NotNull(error);
        Assert.Contains("JSON error", error);
    }

    // --- JSON Minify ---
    [Fact]
    public void MinifyJson_Removes_Whitespace()
    {
        var input = """
        {
            "name": "John",
            "age": 30
        }
        """;
        var (result, error) = FormatHelper.MinifyJson(input);

        Assert.Null(error);
        Assert.DoesNotContain("\n", result);
        Assert.DoesNotContain("  ", result);
    }

    [Fact]
    public void MinifyJson_Returns_Error_For_Invalid_Json()
    {
        var (result, error) = FormatHelper.MinifyJson("not json");

        Assert.NotNull(error);
        Assert.Contains("JSON error", error);
    }

    // --- JSON Round-trip ---
    [Fact]
    public void Json_PrettyPrint_Then_Minify_Preserves_Data()
    {
        var original = """{"a":1,"b":"hello","c":[true,false,null]}""";
        var (pretty, _) = FormatHelper.PrettyPrintJson(original);
        var (minified, _) = FormatHelper.MinifyJson(pretty);

        Assert.Equal(original, minified);
    }

    // --- XML Pretty Print ---
    [Fact]
    public void PrettyPrintXml_Formats_Compact_Xml()
    {
        var input = "<root><item>1</item><item>2</item></root>";
        var (result, error) = FormatHelper.PrettyPrintXml(input);

        Assert.Null(error);
        Assert.Contains("<root>", result);
        Assert.Contains("<item>", result);
    }

    [Fact]
    public void PrettyPrintXml_Returns_Error_For_Invalid_Xml()
    {
        var (result, error) = FormatHelper.PrettyPrintXml("<unclosed>");

        Assert.NotNull(error);
        Assert.Contains("XML error", error);
    }

    // --- XML Minify ---
    [Fact]
    public void MinifyXml_Removes_Whitespace()
    {
        var input = """
        <root>
            <item>1</item>
            <item>2</item>
        </root>
        """;
        var (result, error) = FormatHelper.MinifyXml(input);

        Assert.Null(error);
        Assert.Contains("<root>", result);
        // Should not have indentation
        Assert.DoesNotContain("    <item>", result);
    }

    [Fact]
    public void MinifyXml_Returns_Error_For_Invalid_Xml()
    {
        var (result, error) = FormatHelper.MinifyXml("<bad>xml");

        Assert.NotNull(error);
        Assert.Contains("XML error", error);
    }

    // --- Language Detection ---
    [Theory]
    [InlineData("JSON", true)]
    [InlineData("json", true)]
    [InlineData("XML", false)]
    [InlineData("C#", false)]
    public void IsJsonLanguage_Identifies_Json(string language, bool expected)
    {
        Assert.Equal(expected, FormatHelper.IsJsonLanguage(language));
    }

    [Theory]
    [InlineData("XML", true)]
    [InlineData("HTML", true)]
    [InlineData("html", true)]
    [InlineData("JSON", false)]
    [InlineData("C#", false)]
    public void IsXmlLanguage_Identifies_Xml_And_Html(string language, bool expected)
    {
        Assert.Equal(expected, FormatHelper.IsXmlLanguage(language));
    }

    [Theory]
    [InlineData("JSON", true)]
    [InlineData("XML", true)]
    [InlineData("HTML", true)]
    [InlineData("C#", false)]
    [InlineData("Python", false)]
    [InlineData("", false)]
    public void IsFormattable_Returns_True_For_Supported_Languages(string language, bool expected)
    {
        Assert.Equal(expected, FormatHelper.IsFormattable(language));
    }

    // --- Edge Cases ---
    [Fact]
    public void PrettyPrintJson_Handles_Empty_Object()
    {
        var (result, error) = FormatHelper.PrettyPrintJson("{}");
        Assert.Null(error);
        Assert.Contains("{", result);
    }

    [Fact]
    public void PrettyPrintJson_Handles_Empty_Array()
    {
        var (result, error) = FormatHelper.PrettyPrintJson("[]");
        Assert.Null(error);
        Assert.Contains("[", result);
    }

    [Fact]
    public void PrettyPrintXml_Handles_Self_Closing_Tag()
    {
        var (result, error) = FormatHelper.PrettyPrintXml("<root />");
        Assert.Null(error);
        Assert.Contains("root", result);
    }
}
