namespace FastEdit.Core.FileAnalysis;

public class BinaryDetector
{
    private const int SampleSize = 8192;
    private const double BinaryThreshold = 0.30;

    private static readonly Dictionary<byte[], string> MagicBytes = new()
    {
        // Images
        { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png" },
        { new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg" },
        { new byte[] { 0x47, 0x49, 0x46, 0x38 }, "image/gif" },
        { new byte[] { 0x42, 0x4D }, "image/bmp" },
        { new byte[] { 0x00, 0x00, 0x01, 0x00 }, "image/x-icon" },

        // Archives
        { new byte[] { 0x50, 0x4B, 0x03, 0x04 }, "application/zip" },
        { new byte[] { 0x50, 0x4B, 0x05, 0x06 }, "application/zip" },
        { new byte[] { 0x1F, 0x8B }, "application/gzip" },
        { new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07 }, "application/x-rar" },
        { new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }, "application/x-7z" },
        { new byte[] { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00 }, "application/x-xz" },
        { new byte[] { 0x42, 0x5A, 0x68 }, "application/x-bzip2" },

        // Executables
        { new byte[] { 0x4D, 0x5A }, "application/x-msdownload" },
        { new byte[] { 0x7F, 0x45, 0x4C, 0x46 }, "application/x-elf" },
        { new byte[] { 0xFE, 0xED, 0xFA, 0xCE }, "application/x-mach-binary" },
        { new byte[] { 0xFE, 0xED, 0xFA, 0xCF }, "application/x-mach-binary" },
        { new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }, "application/java-vm" },

        // Documents
        { new byte[] { 0x25, 0x50, 0x44, 0x46 }, "application/pdf" },
        { new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }, "application/msword" },

        // Media
        { new byte[] { 0x49, 0x44, 0x33 }, "audio/mpeg" },
        { new byte[] { 0xFF, 0xFB }, "audio/mpeg" },

        // Database
        { new byte[] { 0x53, 0x51, 0x4C, 0x69, 0x74, 0x65 }, "application/x-sqlite3" },

        // Fonts
        { new byte[] { 0x00, 0x01, 0x00, 0x00 }, "font/ttf" },
        { new byte[] { 0x4F, 0x54, 0x54, 0x4F }, "font/otf" },
        { new byte[] { 0x77, 0x4F, 0x46, 0x46 }, "font/woff" },
        { new byte[] { 0x77, 0x4F, 0x46, 0x32 }, "font/woff2" },
    };

    private static readonly Dictionary<byte[], string> TextBOMs = new()
    {
        { new byte[] { 0xEF, 0xBB, 0xBF }, "UTF-8" },
        { new byte[] { 0xFF, 0xFE }, "UTF-16 LE" },
        { new byte[] { 0xFE, 0xFF }, "UTF-16 BE" },
        { new byte[] { 0xFF, 0xFE, 0x00, 0x00 }, "UTF-32 LE" },
        { new byte[] { 0x00, 0x00, 0xFE, 0xFF }, "UTF-32 BE" },
    };

    public async Task<bool> IsBinaryFileAsync(string filePath)
    {
        var result = await AnalyzeFileAsync(filePath);
        return result.IsBinary;
    }

    public async Task<BinaryAnalysisResult> AnalyzeFileAsync(string filePath)
    {
        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: SampleSize,
                useAsync: true);

            var buffer = new byte[(int)Math.Min(SampleSize, stream.Length)];
            var bytesRead = await stream.ReadAsync(buffer.AsMemory());

            if (bytesRead == 0)
            {
                return new BinaryAnalysisResult
                {
                    IsBinary = false,
                    Reason = BinaryDetectionReason.EmptyFile,
                    Confidence = 1.0
                };
            }

            // Resize buffer if we read less than expected
            if (bytesRead < buffer.Length)
            {
                Array.Resize(ref buffer, bytesRead);
            }

            return AnalyzeBuffer(buffer);
        }
        catch (Exception)
        {
            return new BinaryAnalysisResult
            {
                IsBinary = true,
                Confidence = 0.5
            };
        }
    }

    private static BinaryAnalysisResult AnalyzeBuffer(byte[] buffer)
    {
        var result = new BinaryAnalysisResult();

        // Check for text BOMs
        foreach (var (bom, encoding) in TextBOMs)
        {
            if (StartsWith(buffer, bom))
            {
                result.IsBinary = false;
                result.Reason = BinaryDetectionReason.TextEncodingDetected;
                result.DetectedEncoding = encoding;
                result.Confidence = 0.99;
                return result;
            }
        }

        // Check magic bytes
        foreach (var (magic, mimeType) in MagicBytes)
        {
            if (StartsWith(buffer, magic))
            {
                result.IsBinary = true;
                result.Reason = BinaryDetectionReason.MagicBytesMatch;
                result.DetectedMimeType = mimeType;
                result.Confidence = 0.99;
                return result;
            }
        }

        // Statistical analysis
        var stats = AnalyzeByteStatistics(buffer);

        if (stats.NullByteCount > 0)
        {
            if (IsLikelyUtf16(buffer))
            {
                result.IsBinary = false;
                result.Reason = BinaryDetectionReason.TextEncodingDetected;
                result.DetectedEncoding = "UTF-16";
                result.Confidence = 0.85;
                return result;
            }

            result.IsBinary = true;
            result.Reason = BinaryDetectionReason.NullBytesDetected;
            result.Confidence = 0.95;
            return result;
        }

        if (stats.NonPrintableRatio > BinaryThreshold)
        {
            result.IsBinary = true;
            result.Reason = BinaryDetectionReason.HighNonPrintableRatio;
            result.Confidence = 0.80 + (stats.NonPrintableRatio * 0.2);
            return result;
        }

        result.IsBinary = false;
        result.Reason = BinaryDetectionReason.TextEncodingDetected;
        result.DetectedEncoding = DetectTextEncoding(buffer);
        result.Confidence = 1.0 - stats.NonPrintableRatio;
        return result;
    }

    private static bool StartsWith(byte[] buffer, byte[] prefix)
    {
        if (buffer.Length < prefix.Length)
            return false;

        for (int i = 0; i < prefix.Length; i++)
        {
            if (buffer[i] != prefix[i])
                return false;
        }
        return true;
    }

    private record ByteStatistics(
        int NullByteCount,
        int ControlCharCount,
        int PrintableCount,
        double NonPrintableRatio);

    private static ByteStatistics AnalyzeByteStatistics(byte[] data)
    {
        int nullCount = 0;
        int controlCount = 0;
        int printableCount = 0;

        foreach (var b in data)
        {
            if (b == 0x00)
                nullCount++;
            else if (b < 0x20 && b != 0x09 && b != 0x0A && b != 0x0D)
                controlCount++;
            else if (b >= 0x20 && b < 0x7F)
                printableCount++;
            else if (b >= 0x80)
                printableCount++;
        }

        var totalNonPrintable = nullCount + controlCount;
        var ratio = data.Length > 0 ? (double)totalNonPrintable / data.Length : 0;

        return new ByteStatistics(nullCount, controlCount, printableCount, ratio);
    }

    private static bool IsLikelyUtf16(byte[] data)
    {
        if (data.Length < 4) return false;

        int lePattern = 0;
        int bePattern = 0;

        for (int i = 0; i < data.Length - 1; i += 2)
        {
            if (data[i] >= 0x20 && data[i] < 0x7F && data[i + 1] == 0x00)
                lePattern++;
            if (data[i] == 0x00 && data[i + 1] >= 0x20 && data[i + 1] < 0x7F)
                bePattern++;
        }

        var pairCount = data.Length / 2;
        return lePattern > pairCount * 0.5 || bePattern > pairCount * 0.5;
    }

    private static string DetectTextEncoding(byte[] data)
    {
        if (IsValidUtf8(data))
            return "UTF-8";
        return "ASCII";
    }

    private static bool IsValidUtf8(byte[] data)
    {
        int i = 0;
        while (i < data.Length)
        {
            if (data[i] < 0x80)
            {
                i++;
                continue;
            }

            int continuationBytes;
            if ((data[i] & 0xE0) == 0xC0) continuationBytes = 1;
            else if ((data[i] & 0xF0) == 0xE0) continuationBytes = 2;
            else if ((data[i] & 0xF8) == 0xF0) continuationBytes = 3;
            else return false;

            if (i + continuationBytes >= data.Length)
                return false;

            for (int j = 1; j <= continuationBytes; j++)
            {
                if ((data[i + j] & 0xC0) != 0x80)
                    return false;
            }

            i += continuationBytes + 1;
        }
        return true;
    }
}
