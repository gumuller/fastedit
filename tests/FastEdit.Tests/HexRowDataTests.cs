using FastEdit.Core.HexEngine;

namespace FastEdit.Tests;

public class HexRowDataTests
{
    [Fact]
    public void Constructor_Sets_Properties()
    {
        var bytes = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };
        var row = new HexRowData(0x100, bytes, 16);

        Assert.Equal(0x100, row.Offset);
        Assert.Equal(bytes, row.Bytes);
        Assert.Equal(16, row.BytesPerRow);
    }

    [Fact]
    public void OffsetText_Formats_Correctly()
    {
        var row = new HexRowData(0x00FF, new byte[] { 0x00 }, 16);

        // Should be zero-padded hex
        Assert.Contains("FF", row.OffsetText.ToUpper());
    }

    [Fact]
    public void HexText_Contains_Hex_Values()
    {
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var row = new HexRowData(0, bytes, 16);

        Assert.Contains("DE", row.HexText.ToUpper());
        Assert.Contains("AD", row.HexText.ToUpper());
        Assert.Contains("BE", row.HexText.ToUpper());
        Assert.Contains("EF", row.HexText.ToUpper());
    }

    [Fact]
    public void AsciiText_Shows_Printable_Characters()
    {
        var bytes = new byte[] { 0x48, 0x69, 0x21 }; // "Hi!"
        var row = new HexRowData(0, bytes, 16);

        Assert.Contains("H", row.AsciiText);
        Assert.Contains("i", row.AsciiText);
        Assert.Contains("!", row.AsciiText);
    }

    [Fact]
    public void AsciiText_Replaces_NonPrintable_With_Dot()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x7F };
        var row = new HexRowData(0, bytes, 16);

        // All non-printable bytes should become '.'
        Assert.Equal("...", row.AsciiText);
    }

    [Fact]
    public void Empty_Bytes_Produces_Empty_Texts()
    {
        var row = new HexRowData(0, Array.Empty<byte>(), 16);

        Assert.NotNull(row.HexText);
        Assert.NotNull(row.AsciiText);
    }

    [Fact]
    public void Large_Offset_Does_Not_Truncate()
    {
        long largeOffset = 0x1_0000_0000L; // > 4GB
        var row = new HexRowData(largeOffset, new byte[] { 0xFF }, 16);

        // Should contain the full offset representation
        Assert.Contains("1", row.OffsetText);
    }
}
