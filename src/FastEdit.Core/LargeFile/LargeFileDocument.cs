using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FastEdit.Core.LargeFile;

/// <summary>
/// Memory-mapped, read-only document for multi-GB text files.
/// Builds a hybrid sparse line index (every N lines or M bytes, whichever first)
/// and decodes lines on demand. Handles UTF-8 / UTF-16 BOM / Windows-1252 fallback.
/// </summary>
public sealed class LargeFileDocument : IDisposable
{
    private const int LineCheckpointInterval = 512;
    private const long ByteCheckpointInterval = 2L * 1024 * 1024;
    private const int MaxDisplayLineChars = 10_000;
    private const long PreambleScanBytes = 4096;

    private readonly FileStream _fileStream;
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;

    private long _totalLines;
    private long[] _lineCheckpoints = Array.Empty<long>();

    public string FilePath { get; }
    public long FileSize { get; }
    public Encoding Encoding { get; private set; } = Encoding.UTF8;
    public int BomLength { get; private set; }
    public long TotalLines => _totalLines;
    public bool HasBuiltIndex { get; private set; }

    public LargeFileDocument(string filePath)
    {
        FilePath = filePath;
        var info = new FileInfo(filePath);
        FileSize = info.Length;

        _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (FileSize == 0)
        {
            // Can't create an MMF over a zero-byte file; treat as empty document.
            _mmf = null!;
            _accessor = null!;
            Encoding = new UTF8Encoding(false);
            BomLength = 0;
            return;
        }

        _mmf = MemoryMappedFile.CreateFromFile(
            _fileStream,
            mapName: null,
            capacity: 0,
            MemoryMappedFileAccess.Read,
            HandleInheritability.None,
            leaveOpen: false);
        _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        DetectEncoding();
    }

    private void DetectEncoding()
    {
        if (FileSize == 0 || _accessor == null)
        {
            Encoding = new UTF8Encoding(false);
            BomLength = 0;
            return;
        }

        int scan = (int)Math.Min(FileSize, PreambleScanBytes);
        var buf = new byte[scan];
        _accessor.ReadArray(0, buf, 0, scan);

        if (scan >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF)
        {
            Encoding = new UTF8Encoding(true);
            BomLength = 3;
            return;
        }
        if (scan >= 2 && buf[0] == 0xFF && buf[1] == 0xFE)
        {
            Encoding = Encoding.Unicode;
            BomLength = 2;
            return;
        }
        if (scan >= 2 && buf[0] == 0xFE && buf[1] == 0xFF)
        {
            Encoding = Encoding.BigEndianUnicode;
            BomLength = 2;
            return;
        }

        if (IsValidUtf8(buf, scan))
        {
            Encoding = new UTF8Encoding(false);
            BomLength = 0;
            return;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding = Encoding.GetEncoding(1252);
        BomLength = 0;
    }

    private static bool IsValidUtf8(byte[] bytes, int length)
    {
        int i = 0;
        while (i < length)
        {
            byte b = bytes[i];
            int cont;
            if (b <= 0x7F) { i++; continue; }
            else if (b >= 0xC2 && b <= 0xDF) cont = 1;
            else if (b >= 0xE0 && b <= 0xEF) cont = 2;
            else if (b >= 0xF0 && b <= 0xF4) cont = 3;
            else return false;

            if (i + cont >= length) return true;
            for (int j = 1; j <= cont; j++)
                if ((bytes[i + j] & 0xC0) != 0x80) return false;

            i += cont + 1;
        }
        return true;
    }

    public async Task BuildIndexAsync(IProgress<double>? onProgress, CancellationToken ct)
    {
        await Task.Run(() => BuildIndexSync(onProgress, ct), ct).ConfigureAwait(false);
    }

    private void BuildIndexSync(IProgress<double>? onProgress, CancellationToken ct)
    {
        if (FileSize == 0 || _accessor == null)
        {
            _totalLines = 0;
            _lineCheckpoints = new long[] { 0 };
            HasBuiltIndex = true;
            onProgress?.Report(1.0);
            return;
        }

        var checkpoints = new List<long>(1024);
        long pos = BomLength;
        long lineCount = 1;
        checkpoints.Add(pos);

        long lastCheckpointBytes = pos;
        long linesSinceCheckpoint = 0;
        long lastProgressReport = 0;

        bool isUtf16Le = Encoding.Equals(Encoding.Unicode);
        bool isUtf16Be = Encoding.Equals(Encoding.BigEndianUnicode);
        int stride = (isUtf16Le || isUtf16Be) ? 2 : 1;

        const int BufSize = 4 * 1024 * 1024;
        var buf = new byte[BufSize];

        while (pos < FileSize)
        {
            ct.ThrowIfCancellationRequested();
            int toRead = (int)Math.Min(BufSize, FileSize - pos);
            _accessor.ReadArray(pos, buf, 0, toRead);

            for (int i = 0; i < toRead; i += stride)
            {
                bool isNewline;
                if (isUtf16Le)
                    isNewline = i + 1 < toRead && buf[i] == 0x0A && buf[i + 1] == 0x00;
                else if (isUtf16Be)
                    isNewline = i + 1 < toRead && buf[i] == 0x00 && buf[i + 1] == 0x0A;
                else
                    isNewline = buf[i] == 0x0A;

                if (isNewline)
                {
                    lineCount++;
                    linesSinceCheckpoint++;
                    long absPos = pos + i + stride;

                    bool lineCheckpoint = linesSinceCheckpoint >= LineCheckpointInterval;
                    bool byteCheckpoint = absPos - lastCheckpointBytes >= ByteCheckpointInterval;

                    if (lineCheckpoint || byteCheckpoint)
                    {
                        checkpoints.Add(absPos);
                        lastCheckpointBytes = absPos;
                        linesSinceCheckpoint = 0;
                    }
                }
            }

            pos += toRead;

            if (pos - lastProgressReport >= 8 * 1024 * 1024)
            {
                onProgress?.Report((double)pos / FileSize);
                lastProgressReport = pos;
            }
        }

        if (FileSize == BomLength) lineCount = 0;

        _totalLines = lineCount;
        _lineCheckpoints = checkpoints.ToArray();
        HasBuiltIndex = true;
        onProgress?.Report(1.0);
    }

    public string GetLine(long lineNumber)
    {
        if (!HasBuiltIndex || lineNumber < 1 || lineNumber > _totalLines)
            return string.Empty;

        long approxIdx = (lineNumber - 1) / LineCheckpointInterval;
        if (approxIdx >= _lineCheckpoints.Length) approxIdx = _lineCheckpoints.Length - 1;

        long pos = _lineCheckpoints[approxIdx];
        long currentLine = approxIdx * LineCheckpointInterval + 1;

        bool isUtf16Le = Encoding.Equals(Encoding.Unicode);
        bool isUtf16Be = Encoding.Equals(Encoding.BigEndianUnicode);
        int stride = (isUtf16Le || isUtf16Be) ? 2 : 1;

        while (currentLine < lineNumber && pos < FileSize)
        {
            byte b0 = _accessor.ReadByte(pos);
            bool isNewline;
            if (isUtf16Le)
            {
                byte b1 = pos + 1 < FileSize ? _accessor.ReadByte(pos + 1) : (byte)0;
                isNewline = b0 == 0x0A && b1 == 0x00;
            }
            else if (isUtf16Be)
            {
                byte b1 = pos + 1 < FileSize ? _accessor.ReadByte(pos + 1) : (byte)0;
                isNewline = b0 == 0x00 && b1 == 0x0A;
            }
            else
            {
                isNewline = b0 == 0x0A;
            }

            pos += stride;
            if (isNewline) currentLine++;
        }

        long lineStart = pos;
        long maxBytes = MaxDisplayLineChars * (long)stride * 4;
        long lineEndScanLimit = Math.Min(FileSize, lineStart + maxBytes);
        long lineEnd = lineEndScanLimit;
        bool truncated = false;

        while (pos < lineEndScanLimit)
        {
            byte b0 = _accessor.ReadByte(pos);
            bool isNewline;
            if (isUtf16Le)
            {
                byte b1 = pos + 1 < FileSize ? _accessor.ReadByte(pos + 1) : (byte)0;
                isNewline = b0 == 0x0A && b1 == 0x00;
            }
            else if (isUtf16Be)
            {
                byte b1 = pos + 1 < FileSize ? _accessor.ReadByte(pos + 1) : (byte)0;
                isNewline = b0 == 0x00 && b1 == 0x0A;
            }
            else
            {
                isNewline = b0 == 0x0A;
            }

            if (isNewline)
            {
                lineEnd = pos;
                break;
            }
            pos += stride;
        }

        if (pos >= lineEndScanLimit && lineEndScanLimit < FileSize)
        {
            truncated = true;
            lineEnd = lineEndScanLimit;
        }

        int byteLen = (int)(lineEnd - lineStart);
        if (byteLen <= 0) return string.Empty;

        var buf = new byte[byteLen];
        _accessor.ReadArray(lineStart, buf, 0, byteLen);

        int trim = 0;
        if (!isUtf16Le && !isUtf16Be && byteLen > 0 && buf[byteLen - 1] == 0x0D) trim = 1;

        string text = Encoding.GetString(buf, 0, byteLen - trim);

        if (text.Length > MaxDisplayLineChars)
            text = text.Substring(0, MaxDisplayLineChars) + " …[truncated]";
        else if (truncated)
            text += " …[truncated]";

        return text;
    }

    public readonly record struct SearchMatch(long LineNumber, int ColumnInLine);

    public async Task<List<SearchMatch>> SearchAsync(
        string needle,
        bool caseSensitive,
        int maxResults,
        IProgress<double>? onProgress,
        CancellationToken ct)
    {
        return await Task.Run(() => SearchSync(needle, caseSensitive, maxResults, onProgress, ct), ct)
            .ConfigureAwait(false);
    }

    private List<SearchMatch> SearchSync(string needle, bool caseSensitive, int maxResults,
        IProgress<double>? onProgress, CancellationToken ct)
    {
        var results = new List<SearchMatch>();
        if (string.IsNullOrEmpty(needle) || !HasBuiltIndex) return results;

        byte[] needleBytes = Encoding.GetBytes(needle);
        byte[]? needleLowerBytes = caseSensitive ? null : Encoding.GetBytes(needle.ToLowerInvariant());

        bool isUtf16Le = Encoding.Equals(Encoding.Unicode);
        bool isUtf16Be = Encoding.Equals(Encoding.BigEndianUnicode);
        int stride = (isUtf16Le || isUtf16Be) ? 2 : 1;

        const int BufSize = 4 * 1024 * 1024;
        int overlap = needleBytes.Length;
        var buf = new byte[BufSize + overlap];

        long pos = BomLength;
        long currentLine = 1;
        long lineStart = BomLength;
        long lastProgressReport = 0;

        while (pos < FileSize)
        {
            ct.ThrowIfCancellationRequested();
            int toRead = (int)Math.Min(BufSize + overlap, FileSize - pos);
            _accessor.ReadArray(pos, buf, 0, toRead);

            int scanLimit = (pos + toRead >= FileSize) ? toRead : toRead - overlap;

            for (int i = 0; i < scanLimit; i += stride)
            {
                bool isNewline;
                if (isUtf16Le) isNewline = i + 1 < toRead && buf[i] == 0x0A && buf[i + 1] == 0x00;
                else if (isUtf16Be) isNewline = i + 1 < toRead && buf[i] == 0x00 && buf[i + 1] == 0x0A;
                else isNewline = buf[i] == 0x0A;

                if (isNewline)
                {
                    currentLine++;
                    lineStart = pos + i + stride;
                }

                if (i + needleBytes.Length <= toRead && MatchAt(buf, i, needleBytes, needleLowerBytes, caseSensitive))
                {
                    long absOffset = pos + i;
                    int col = (int)((absOffset - lineStart) / stride);
                    results.Add(new SearchMatch(currentLine, col));
                    if (results.Count >= maxResults) return results;
                }
            }

            pos += scanLimit;

            if (pos - lastProgressReport >= 16 * 1024 * 1024)
            {
                onProgress?.Report((double)pos / FileSize);
                lastProgressReport = pos;
            }
        }

        onProgress?.Report(1.0);
        return results;
    }

    private static bool MatchAt(byte[] haystack, int offset, byte[] needle, byte[]? needleLower, bool caseSensitive)
    {
        if (caseSensitive)
        {
            for (int i = 0; i < needle.Length; i++)
                if (haystack[offset + i] != needle[i]) return false;
            return true;
        }
        else
        {
            for (int i = 0; i < needle.Length; i++)
            {
                byte h = haystack[offset + i];
                if (h >= 0x41 && h <= 0x5A) h = (byte)(h + 32);
                if (h != needleLower![i]) return false;
            }
            return true;
        }
    }

    public string EncodingDisplayName => Encoding switch
    {
        UTF8Encoding u8 => u8.Preamble.Length > 0 ? "UTF-8 with BOM" : "UTF-8",
        _ when Encoding.Equals(Encoding.Unicode) => "UTF-16 LE",
        _ when Encoding.Equals(Encoding.BigEndianUnicode) => "UTF-16 BE",
        _ => Encoding.EncodingName
    };

    public void Dispose()
    {
        _accessor?.Dispose();
        _mmf?.Dispose();
        _fileStream?.Dispose();
    }
}
