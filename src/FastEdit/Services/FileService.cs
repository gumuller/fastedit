using System.IO;
using System.Text;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public class FileService : IFileService
{
    private const int EncodingDetectionSampleSize = 4096;

    public async Task<string> ReadAllTextAsync(string filePath)
    {
        return await File.ReadAllTextAsync(filePath);
    }

    public async Task WriteAllTextAsync(string filePath, string content)
    {
        await File.WriteAllTextAsync(filePath, content);
    }

    public async Task<FileReadResult> ReadFileWithEncodingAsync(string filePath)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 81920,
            useAsync: true);

        var sampleLength = (int)Math.Min(EncodingDetectionSampleSize, stream.Length);
        var sample = new byte[sampleLength];
        var bytesRead = sampleLength == 0
            ? 0
            : await ReadSampleAsync(stream, sample);

        var sampleCoversEntireFile = bytesRead == stream.Length;
        var (encoding, hasBom) = DetectEncoding(sample.AsSpan(0, bytesRead), sampleCoversEntireFile);
        stream.Position = hasBom ? encoding.GetPreamble().Length : 0;

        using var reader = new StreamReader(
            stream,
            encoding,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 81920,
            leaveOpen: false);

        var content = await reader.ReadToEndAsync();
        return new FileReadResult(content, encoding, hasBom);
    }

    private static async Task<int> ReadSampleAsync(Stream stream, byte[] sample)
    {
        var totalRead = 0;
        while (totalRead < sample.Length)
        {
            var read = await stream.ReadAsync(sample.AsMemory(totalRead));
            if (read == 0)
                break;

            totalRead += read;
        }

        return totalRead;
    }

    public async Task WriteFileWithEncodingAsync(string filePath, string content, Encoding encoding, bool writeBom)
    {
        var encodingToUse = writeBom ? encoding : GetEncodingWithoutBom(encoding);
        await File.WriteAllTextAsync(filePath, content, encodingToUse);
    }

    private static (Encoding encoding, bool hasBom) DetectEncoding(ReadOnlySpan<byte> bytes, bool sampleCoversEntireFile)
    {
        if (bytes.Length == 0)
            return (new UTF8Encoding(false), false);

        // Check BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(true), true);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (Encoding.Unicode, true); // UTF-16 LE

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (Encoding.BigEndianUnicode, true); // UTF-16 BE

        // Check if valid UTF-8
        if (IsValidUtf8(bytes, sampleCoversEntireFile))
            return (new UTF8Encoding(false), false);

        // Fallback to system default (Windows-1252 on Windows)
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return (Encoding.GetEncoding(1252), false);
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes, bool sampleCoversEntireFile)
    {
        int i = 0;
        while (i < bytes.Length)
        {
            byte b = bytes[i];
            int continuationBytes;

            if (b <= 0x7F) { i++; continue; }
            else if (b >= 0xC2 && b <= 0xDF) { continuationBytes = 1; }
            else if (b >= 0xE0 && b <= 0xEF) { continuationBytes = 2; }
            else if (b >= 0xF0 && b <= 0xF4) { continuationBytes = 3; }
            else return false; // Invalid UTF-8 lead byte

            if (i + continuationBytes >= bytes.Length)
                return !sampleCoversEntireFile;

            for (int j = 1; j <= continuationBytes; j++)
            {
                if ((bytes[i + j] & 0xC0) != 0x80) return false;
            }

            i += continuationBytes + 1;
        }

        // Pure ASCII is also valid UTF-8, treat as UTF-8
        return true;
    }

    private static Encoding GetEncodingWithoutBom(Encoding encoding)
    {
        if (encoding is UTF8Encoding)
            return new UTF8Encoding(false);
        // For other encodings, return as-is (UTF-16 always has BOM in .NET)
        return encoding;
    }
}
