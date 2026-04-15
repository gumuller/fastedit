using FastEdit.Services;
using FastEdit.Services.Interfaces;
using FluentAssertions;

namespace FastEdit.Tests;

public class TextToolsServiceTests
{
    private readonly ITextToolsService _sut = new TextToolsService();

    // --- Case Transformations ---

    [Fact]
    public void ToUpperCase_ConvertsText()
    {
        var result = _sut.ToUpperCase("Hello World");
        result.Success.Should().BeTrue();
        result.Text.Should().Be("HELLO WORLD");
    }

    [Fact]
    public void ToLowerCase_ConvertsText()
    {
        var result = _sut.ToLowerCase("Hello World");
        result.Success.Should().BeTrue();
        result.Text.Should().Be("hello world");
    }

    [Fact]
    public void ToTitleCase_ConvertsText()
    {
        var result = _sut.ToTitleCase("hello world foo");
        result.Success.Should().BeTrue();
        result.Text.Should().Be("Hello World Foo");
    }

    [Fact]
    public void InvertCase_InvertsEachCharacter()
    {
        var result = _sut.InvertCase("Hello World");
        result.Success.Should().BeTrue();
        result.Text.Should().Be("hELLO wORLD");
    }

    [Fact]
    public void InvertCase_EmptyString_ReturnsEmpty()
    {
        var result = _sut.InvertCase("");
        result.Success.Should().BeTrue();
        result.Text.Should().BeEmpty();
    }

    // --- Line Operations ---

    [Fact]
    public void RemoveDuplicateLines_RemovesDuplicates()
    {
        var result = _sut.RemoveDuplicateLines("alpha\nbeta\nalpha\ngamma\nbeta");
        result.Success.Should().BeTrue();
        result.Text.Should().Be("alpha\nbeta\ngamma");
        result.Message.Should().Contain("2");
    }

    [Fact]
    public void RemoveDuplicateLines_PreservesWindowsLineEndings()
    {
        var result = _sut.RemoveDuplicateLines("a\r\nb\r\na");
        result.Text.Should().Be("a\r\nb");
    }

    [Fact]
    public void RemoveDuplicateLines_NoDuplicates_ReturnsOriginal()
    {
        var result = _sut.RemoveDuplicateLines("a\nb\nc");
        result.Text.Should().Be("a\nb\nc");
        result.Message.Should().Contain("0");
    }

    [Fact]
    public void SortLinesAscending_SortsText()
    {
        var result = _sut.SortLinesAscending("cherry\napple\nbanana");
        result.Success.Should().BeTrue();
        result.Text.Should().Be("apple\nbanana\ncherry");
    }

    [Fact]
    public void SortLinesDescending_SortsText()
    {
        var result = _sut.SortLinesDescending("cherry\napple\nbanana");
        result.Success.Should().BeTrue();
        result.Text.Should().Be("cherry\nbanana\napple");
    }

    [Fact]
    public void TrimTrailingWhitespace_TrimsEachLine()
    {
        var result = _sut.TrimTrailingWhitespace("hello   \nworld  \n  both  ");
        result.Text.Should().Be("hello\nworld\n  both");
    }

    [Fact]
    public void TrimLeadingWhitespace_TrimsEachLine()
    {
        var result = _sut.TrimLeadingWhitespace("  hello\n  world\n  both  ");
        result.Text.Should().Be("hello\nworld\nboth  ");
    }

    [Fact]
    public void TrimAllWhitespace_TrimsBothSides()
    {
        var result = _sut.TrimAllWhitespace("  hello  \n  world  ");
        result.Text.Should().Be("hello\nworld");
    }

    // --- Indentation ---

    [Fact]
    public void TabsToSpaces_ConvertsTabs()
    {
        var result = _sut.TabsToSpaces("\tindented\n\t\tdouble");
        result.Text.Should().Be("    indented\n        double");
    }

    [Fact]
    public void TabsToSpaces_CustomTabWidth()
    {
        var result = _sut.TabsToSpaces("\tline", 2);
        result.Text.Should().Be("  line");
    }

    [Fact]
    public void SpacesToTabs_ConvertsLeadingSpaces()
    {
        var result = _sut.SpacesToTabs("    indented\n        double");
        result.Text.Should().Be("\tindented\n\t\tdouble");
    }

    [Fact]
    public void SpacesToTabs_DoesNotConvertInlineSpaces()
    {
        var result = _sut.SpacesToTabs("    hello    world");
        result.Text.Should().Be("\thello    world");
    }

    // --- Encoding ---

    [Fact]
    public void Base64Encode_EncodesText()
    {
        var result = _sut.Base64Encode("Hello World");
        result.Success.Should().BeTrue();
        result.Text.Should().Be("SGVsbG8gV29ybGQ=");
    }

    [Fact]
    public void Base64Decode_DecodesText()
    {
        var result = _sut.Base64Decode("SGVsbG8gV29ybGQ=");
        result.Success.Should().BeTrue();
        result.Text.Should().Be("Hello World");
    }

    [Fact]
    public void Base64Decode_InvalidInput_ReturnsFail()
    {
        var result = _sut.Base64Decode("not-valid-base64!!!");
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid");
    }

    [Fact]
    public void Base64Decode_TrimsWhitespace()
    {
        var result = _sut.Base64Decode("  SGVsbG8gV29ybGQ=  ");
        result.Success.Should().BeTrue();
        result.Text.Should().Be("Hello World");
    }

    [Fact]
    public void UrlEncode_EncodesSpecialCharacters()
    {
        var result = _sut.UrlEncode("hello world&foo=bar");
        result.Success.Should().BeTrue();
        result.Text.Should().Be("hello%20world%26foo%3Dbar");
    }

    [Fact]
    public void UrlDecode_DecodesText()
    {
        var result = _sut.UrlDecode("hello%20world%26foo%3Dbar");
        result.Success.Should().BeTrue();
        result.Text.Should().Be("hello world&foo=bar");
    }

    // --- Checksums ---

    [Fact]
    public void ComputeMd5_ReturnsCorrectHash()
    {
        var result = _sut.ComputeMd5("Hello World");
        result.Success.Should().BeTrue();
        result.Text.Should().Be("b10a8db164e0754105b7a99be72e3fe5");
    }

    [Fact]
    public void ComputeSha1_ReturnsCorrectHash()
    {
        var result = _sut.ComputeSha1("Hello World");
        result.Success.Should().BeTrue();
        result.Text.Should().Be("0a4d55a8d778e5022fab701977c5d840bbc486d0");
    }

    [Fact]
    public void ComputeSha256_ReturnsCorrectHash()
    {
        var result = _sut.ComputeSha256("Hello World");
        result.Success.Should().BeTrue();
        result.Text.Should().Be("a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e");
    }

    [Fact]
    public void ComputeSha512_ReturnsCorrectHash()
    {
        var result = _sut.ComputeSha512("Hello World");
        result.Success.Should().BeTrue();
        result.Text.Should().HaveLength(128); // SHA-512 = 64 bytes = 128 hex chars
    }

    [Fact]
    public void ComputeMd5_EmptyString_ReturnsHash()
    {
        var result = _sut.ComputeMd5("");
        result.Success.Should().BeTrue();
        result.Text.Should().Be("d41d8cd98f00b204e9800998ecf8427e");
    }

    // --- Edge Cases ---

    [Fact]
    public void ToUpperCase_EmptyString_ReturnsEmpty()
    {
        var result = _sut.ToUpperCase("");
        result.Success.Should().BeTrue();
        result.Text.Should().BeEmpty();
    }

    [Fact]
    public void SortLinesAscending_SingleLine_ReturnsSame()
    {
        var result = _sut.SortLinesAscending("only one line");
        result.Text.Should().Be("only one line");
    }

    [Fact]
    public void RemoveDuplicateLines_EmptyLines()
    {
        var result = _sut.RemoveDuplicateLines("a\n\nb\n\nc");
        result.Text.Should().Be("a\n\nb\nc");
    }
}
