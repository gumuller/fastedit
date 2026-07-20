using System.Buffers.Binary;
using System.IO;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

internal static class LosslessTextSnapshotCodec
{
    private const int HeaderLength = 12;
    private const int MaxCharacters = 128 * 1024 * 1024;
    private const long MaxLegacyBytes = (long)MaxCharacters * sizeof(char) * 2;
    private static readonly byte[] Magic = "FETXT001"u8.ToArray();

    public const string Format = "utf16-code-units-v1";

    public static void Write(Stream stream, string content)
    {
        if (content.Length > MaxCharacters)
            throw new InvalidDataException("The text snapshot exceeds the supported size.");

        Span<byte> header = stackalloc byte[HeaderLength];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(
            header[Magic.Length..],
            content.Length);
        stream.Write(header);
        Span<byte> codeUnit = stackalloc byte[2];
        foreach (var character in content)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(codeUnit, character);
            stream.Write(codeUnit);
        }
    }

    public static async Task<string> ReadAsync(
        IFileSystemService fileSystem,
        string snapshotPath,
        string? snapshotFormat)
    {
        if (string.Equals(snapshotFormat, Format, StringComparison.Ordinal))
            return ReadFramed(fileSystem, snapshotPath);
        if (!string.IsNullOrEmpty(snapshotFormat))
            throw new InvalidDataException("The text snapshot format is unsupported.");
        if (fileSystem.GetFileSize(snapshotPath) > MaxLegacyBytes)
        {
            throw new InvalidDataException(
                "The legacy text snapshot exceeds the supported size.");
        }

        return await fileSystem.ReadAllTextAsync(snapshotPath);
    }

    private static string ReadFramed(
        IFileSystemService fileSystem,
        string snapshotPath)
    {
        using var stream = fileSystem.OpenRead(snapshotPath);
        Span<byte> magic = stackalloc byte[Magic.Length];
        if (ReadUpTo(stream, magic) != Magic.Length ||
            !magic.SequenceEqual(Magic))
        {
            throw new InvalidDataException(
                "The text snapshot frame has an invalid signature.");
        }

        Span<byte> lengthBytes = stackalloc byte[sizeof(int)];
        ReadExactly(stream, lengthBytes);
        var characterCount =
            BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (characterCount < 0 ||
            characterCount > MaxCharacters ||
            stream.Length != HeaderLength + (long)characterCount * sizeof(char))
        {
            throw new InvalidDataException(
                "The text snapshot frame is invalid.");
        }

        var characters = new char[characterCount];
        Span<byte> codeUnit = stackalloc byte[2];
        for (var index = 0; index < characters.Length; index++)
        {
            ReadExactly(stream, codeUnit);
            characters[index] = (char)
                BinaryPrimitives.ReadUInt16LittleEndian(codeUnit);
        }

        return new string(characters);
    }

    private static int ReadUpTo(Stream stream, Span<byte> buffer)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = stream.Read(buffer[totalRead..]);
            if (read == 0)
                break;
            totalRead += read;
        }
        return totalRead;
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        if (ReadUpTo(stream, buffer) != buffer.Length)
            throw new InvalidDataException("The text snapshot frame is truncated.");
    }
}
