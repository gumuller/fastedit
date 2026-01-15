namespace FastEdit.Core.HexEngine;

public class HexRowData
{
    public long Offset { get; }
    public byte[] Bytes { get; }
    public int BytesPerRow { get; }
    public string OffsetText { get; }
    public string HexText { get; }
    public string AsciiText { get; }

    public HexRowData(long offset, byte[] bytes, int bytesPerRow)
    {
        Offset = offset;
        Bytes = bytes;
        BytesPerRow = bytesPerRow;
        OffsetText = offset.ToString("X8");
        HexText = FormatHex(bytes, bytesPerRow);
        AsciiText = FormatAscii(bytes);
    }

    private static string FormatHex(byte[] bytes, int bytesPerRow)
    {
        var sb = new System.Text.StringBuilder(bytesPerRow * 3);
        for (int i = 0; i < bytesPerRow; i++)
        {
            if (i < bytes.Length)
                sb.Append(bytes[i].ToString("X2"));
            else
                sb.Append("  ");

            if (i < bytesPerRow - 1)
            {
                sb.Append(' ');
                if ((i + 1) % 8 == 0)
                    sb.Append(' ');
            }
        }
        return sb.ToString();
    }

    private static string FormatAscii(byte[] bytes)
    {
        var sb = new System.Text.StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            if (b >= 0x20 && b < 0x7F)
                sb.Append((char)b);
            else
                sb.Append('.');
        }
        return sb.ToString();
    }
}
