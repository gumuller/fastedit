using FastEdit.Core.HexEngine;

namespace FastEdit.Tests;

public class HexSearchQueryParserTests
{
    [Fact]
    public void Parse_EmptyInput_ReturnsNull()
    {
        Assert.Null(HexSearchQueryParser.Parse(""));
        Assert.Null(HexSearchQueryParser.Parse("   "));
    }

    [Fact]
    public void Parse_QuotedText_ReturnsUtf8Bytes()
    {
        var bytes = HexSearchQueryParser.Parse("\"Hello\"");

        Assert.Equal(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }, bytes);
    }

    [Fact]
    public void Parse_QuotedUnicodeText_ReturnsUtf8Bytes()
    {
        var bytes = HexSearchQueryParser.Parse("\"cafe\"");

        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("cafe"), bytes);
    }

    [Fact]
    public void Parse_ContinuousHex_ReturnsBytes()
    {
        var bytes = HexSearchQueryParser.Parse("48656C6C6F");

        Assert.Equal(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }, bytes);
    }

    [Fact]
    public void Parse_SpaceSeparatedHex_ReturnsBytes()
    {
        var bytes = HexSearchQueryParser.Parse("48 65 6C 6C 6F");

        Assert.Equal(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }, bytes);
    }

    [Fact]
    public void Parse_HyphenatedHex_ReturnsBytes()
    {
        var bytes = HexSearchQueryParser.Parse("48-65-6C-6C-6F");

        Assert.Equal(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }, bytes);
    }

    [Fact]
    public void Parse_LowercaseHex_ReturnsBytes()
    {
        var bytes = HexSearchQueryParser.Parse("48 65 6c 6c 6f");

        Assert.Equal(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }, bytes);
    }

    [Fact]
    public void Parse_OddLengthHex_ReturnsNull()
    {
        Assert.Null(HexSearchQueryParser.Parse("ABC"));
    }

    [Fact]
    public void Parse_InvalidHex_ReturnsNull()
    {
        Assert.Null(HexSearchQueryParser.Parse("GG"));
    }
}
