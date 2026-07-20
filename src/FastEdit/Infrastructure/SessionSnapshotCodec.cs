using System.IO;

namespace FastEdit.Infrastructure;

internal static class SessionSnapshotCodec
{
    public const int CurrentVersion = 2;

    public static string EncodeText(string content)
    {
        var bytes = new byte[content.Length * sizeof(char)];
        for (var index = 0; index < content.Length; index++)
        {
            var codeUnit = content[index];
            bytes[index * 2] = (byte)codeUnit;
            bytes[(index * 2) + 1] = (byte)(codeUnit >> 8);
        }

        return Convert.ToBase64String(bytes);
    }

    public static string DecodeText(string payload)
    {
        var bytes = Convert.FromBase64String(payload);
        if ((bytes.Length & 1) != 0)
            throw new InvalidDataException("The UTF-16 snapshot payload has an odd byte length.");

        var chars = new char[bytes.Length / sizeof(char)];
        for (var index = 0; index < chars.Length; index++)
            chars[index] = (char)(bytes[index * 2] | (bytes[(index * 2) + 1] << 8));

        return new string(chars);
    }

    public static string DecodeLegacyUtf8(byte[] bytes)
    {
        var chars = new List<char>(bytes.Length);
        for (var index = 0; index < bytes.Length;)
        {
            var first = bytes[index];
            if (first <= 0x7F)
            {
                chars.Add((char)first);
                index++;
                continue;
            }

            if (first is >= 0xC2 and <= 0xDF &&
                HasContinuation(bytes, index + 1))
            {
                chars.Add((char)(((first & 0x1F) << 6) |
                    (bytes[index + 1] & 0x3F)));
                index += 2;
                continue;
            }

            if (first is >= 0xE0 and <= 0xEF &&
                HasContinuation(bytes, index + 1) &&
                HasContinuation(bytes, index + 2))
            {
                var codeUnit = ((first & 0x0F) << 12) |
                    ((bytes[index + 1] & 0x3F) << 6) |
                    (bytes[index + 2] & 0x3F);
                if (codeUnit >= 0x800)
                {
                    chars.Add((char)codeUnit);
                    index += 3;
                    continue;
                }
            }

            if (first is >= 0xF0 and <= 0xF4 &&
                HasContinuation(bytes, index + 1) &&
                HasContinuation(bytes, index + 2) &&
                HasContinuation(bytes, index + 3))
            {
                var scalar = ((first & 0x07) << 18) |
                    ((bytes[index + 1] & 0x3F) << 12) |
                    ((bytes[index + 2] & 0x3F) << 6) |
                    (bytes[index + 3] & 0x3F);
                if (scalar is >= 0x10000 and <= 0x10FFFF)
                {
                    scalar -= 0x10000;
                    chars.Add((char)(0xD800 | (scalar >> 10)));
                    chars.Add((char)(0xDC00 | (scalar & 0x3FF)));
                    index += 4;
                    continue;
                }
            }

            chars.Add('\uFFFD');
            index++;
        }

        return new string(chars.ToArray());
    }

    private static bool HasContinuation(byte[] bytes, int index)
    {
        return index < bytes.Length && (bytes[index] & 0xC0) == 0x80;
    }
}
