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
/// and decodes lines on demand. Handles UTF-8 / UTF-16 BOM / ISO-8859-1 fallback.
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
    private LineCheckpoint[] _lineCheckpoints = Array.Empty<LineCheckpoint>();

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

        Encoding = Encoding.Latin1;
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
            _lineCheckpoints = new[] { new LineCheckpoint(1, 0) };
            HasBuiltIndex = true;
            onProgress?.Report(1.0);
            return;
        }

        var checkpoints = new List<LineCheckpoint>(1024);
        long pos = BomLength;
        long lineCount = 1;
        checkpoints.Add(new LineCheckpoint(lineCount, pos));

        long lastCheckpointBytes = pos;
        long linesSinceCheckpoint = 0;
        long lastProgressReport = 0;

        var layout = EncodingLayout.From(Encoding);

        const int BufSize = 4 * 1024 * 1024;
        var buf = new byte[BufSize];

        while (pos < FileSize)
        {
            ct.ThrowIfCancellationRequested();
            int toRead = (int)Math.Min(BufSize, FileSize - pos);
            _accessor.ReadArray(pos, buf, 0, toRead);

            for (int i = 0; i < toRead; i += layout.Stride)
            {
                if (layout.IsNewline(buf, i, toRead))
                {
                    lineCount++;
                    linesSinceCheckpoint++;
                    long absPos = pos + i + layout.Stride;

                    if (ShouldCreateCheckpoint(linesSinceCheckpoint, absPos, lastCheckpointBytes))
                    {
                        checkpoints.Add(new LineCheckpoint(lineCount, absPos));
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

    private static bool ShouldCreateCheckpoint(long linesSinceCheckpoint, long byteOffset, long lastCheckpointBytes) =>
        linesSinceCheckpoint >= LineCheckpointInterval ||
        byteOffset - lastCheckpointBytes >= ByteCheckpointInterval;

    public string GetLine(long lineNumber)
    {
        if (!HasBuiltIndex || lineNumber < 1 || lineNumber > _totalLines)
            return string.Empty;

        var layout = EncodingLayout.From(Encoding);
        var lineStart = FindLineStart(lineNumber, FindCheckpointForLine(lineNumber), layout);
        var lineEnd = FindLineEnd(lineStart, layout, out var truncated);
        return DecodeDisplayLine(lineStart, lineEnd, truncated, layout);
    }

    private long FindLineStart(long lineNumber, LineCheckpoint checkpoint, EncodingLayout layout)
    {
        var pos = checkpoint.ByteOffset;
        var currentLine = checkpoint.LineNumber;

        while (currentLine < lineNumber && pos < FileSize)
        {
            var isNewline = layout.IsNewline(_accessor, pos, FileSize);
            pos += layout.Stride;
            if (isNewline) currentLine++;
        }

        return pos;
    }

    private long FindLineEnd(long lineStart, EncodingLayout layout, out bool truncated)
    {
        var pos = lineStart;
        var lineEndScanLimit = Math.Min(FileSize, lineStart + MaxDisplayLineChars * (long)layout.Stride * 4);
        var lineEnd = lineEndScanLimit;
        truncated = false;

        while (pos < lineEndScanLimit)
        {
            if (layout.IsNewline(_accessor, pos, FileSize))
            {
                lineEnd = pos;
                break;
            }

            pos += layout.Stride;
        }

        if (pos < lineEndScanLimit || lineEndScanLimit >= FileSize)
            return lineEnd;

        truncated = true;
        return lineEndScanLimit;
    }

    private string DecodeDisplayLine(long lineStart, long lineEnd, bool truncated, EncodingLayout layout)
    {
        var byteLen = (int)(lineEnd - lineStart);
        if (byteLen <= 0) return string.Empty;

        var buf = new byte[byteLen];
        _accessor.ReadArray(lineStart, buf, 0, byteLen);

        var trim = GetTrailingCarriageReturnByteCount(buf, 0, byteLen, layout);
        var text = Encoding.GetString(buf, 0, byteLen - trim);

        return text.Length > MaxDisplayLineChars
            ? text.Substring(0, MaxDisplayLineChars) + " …[truncated]"
            : truncated ? text + " …[truncated]" : text;
    }

    public readonly record struct SearchMatch(long LineNumber, int ColumnInLine);

    private readonly record struct LineCheckpoint(long LineNumber, long ByteOffset);

    private LineCheckpoint FindCheckpointForLine(long lineNumber)
    {
        var low = 0;
        var high = _lineCheckpoints.Length - 1;
        var best = _lineCheckpoints[0];

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var checkpoint = _lineCheckpoints[mid];

            if (checkpoint.LineNumber <= lineNumber)
            {
                best = checkpoint;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return best;
    }

    public async Task<List<long>> FindMatchingLinesAsync(
        Func<string, bool> predicate,
        int maxResults,
        IProgress<double>? onProgress,
        CancellationToken ct)
    {
        return await Task.Run(() => FindMatchingLinesSync(predicate, maxResults, onProgress, ct), ct)
            .ConfigureAwait(false);
    }

    private List<long> FindMatchingLinesSync(Func<string, bool> predicate, int maxResults,
        IProgress<double>? onProgress, CancellationToken ct)
    {
        var results = new List<long>();
        if (!HasBuiltIndex || FileSize == 0 || _accessor == null) return results;

        var layout = EncodingLayout.From(Encoding);

        const int BufSize = 4 * 1024 * 1024;
        var buf = new byte[BufSize];

        long pos = BomLength;
        long currentLine = 1;
        long lastProgressReport = 0;

        // Carry holds the tail of the previous buffer for a line spanning the boundary.
        var carry = new List<byte>(4096);

        while (pos < FileSize)
        {
            ct.ThrowIfCancellationRequested();
            int toRead = (int)Math.Min(BufSize, FileSize - pos);
            _accessor.ReadArray(pos, buf, 0, toRead);

            int lineStart = 0;
            for (int i = 0; i < toRead; i += layout.Stride)
            {
                if (layout.IsNewline(buf, i, toRead))
                {
                    int byteLen = i - lineStart;
                    var line = DecodeBufferedLine(buf, lineStart, byteLen, carry, layout);
                    if (AddMatchingLine(results, currentLine, line, predicate, maxResults))
                        return results;

                    currentLine++;
                    lineStart = i + layout.Stride;
                }
            }

            // Carry over the tail (partial line at end of buffer).
            if (lineStart < toRead)
            {
                int tailLen = toRead - lineStart;
                // Cap carry to avoid pathological memory use on a single giant line.
                if (carry.Count + tailLen <= 4 * 1024 * 1024)
                    carry.AddRange(new ArraySegment<byte>(buf, lineStart, tailLen));
            }

            pos += toRead;

            if (pos - lastProgressReport >= 16 * 1024 * 1024)
            {
                onProgress?.Report((double)pos / FileSize);
                lastProgressReport = pos;
            }
        }

        // Final line without trailing newline.
        if (carry.Count > 0)
        {
            var line = DecodeLine(carry.ToArray(), 0, carry.Count, layout);
            AddMatchingLine(results, currentLine, line, predicate, maxResults);
        }

        onProgress?.Report(1.0);
        return results;
    }

    private string DecodeBufferedLine(byte[] buf, int offset, int length, List<byte> carry, EncodingLayout layout)
    {
        if (carry.Count == 0)
            return DecodeLine(buf, offset, length, layout);

        carry.AddRange(new ArraySegment<byte>(buf, offset, length));
        var line = DecodeLine(carry.ToArray(), 0, carry.Count, layout);
        carry.Clear();
        return line;
    }

    private static bool AddMatchingLine(
        List<long> results,
        long lineNumber,
        string line,
        Func<string, bool> predicate,
        int maxResults)
    {
        if (!predicate(line))
            return false;

        results.Add(lineNumber);
        return results.Count >= maxResults;
    }

    private string DecodeLine(byte[] buf, int offset, int length, EncodingLayout layout)
    {
        length -= GetTrailingCarriageReturnByteCount(buf, offset, length, layout);
        if (length <= 0) return string.Empty;
        return Encoding.GetString(buf, offset, length);
    }

    private static int GetTrailingCarriageReturnByteCount(
        byte[] buf,
        int offset,
        int length,
        EncodingLayout layout)
    {
        if (length <= 0)
            return 0;

        if (!layout.IsUtf16Le && !layout.IsUtf16Be)
            return buf[offset + length - 1] == 0x0D ? 1 : 0;

        if (length < 2)
            return 0;

        var last = offset + length - 2;
        if (layout.IsUtf16Le)
            return buf[last] == 0x0D && buf[last + 1] == 0x00 ? 2 : 0;

        return buf[last] == 0x00 && buf[last + 1] == 0x0D ? 2 : 0;
    }


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

        var query = SearchNeedle.Create(needle, caseSensitive, Encoding);
        var layout = EncodingLayout.From(Encoding);

        const int BufSize = 4 * 1024 * 1024;
        int overlap = query.Bytes.Length;
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

            for (int i = 0; i < scanLimit; i += layout.Stride)
            {
                if (layout.IsNewline(buf, i, toRead))
                {
                    currentLine++;
                    lineStart = pos + i + layout.Stride;
                }

                if (TryAddSearchMatch(results, currentLine, lineStart, pos, buf, i, toRead, query, layout, maxResults))
                    return results;
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

    private static bool TryAddSearchMatch(
        List<SearchMatch> results,
        long currentLine,
        long lineStart,
        long bufferStart,
        byte[] buffer,
        int offset,
        int bytesRead,
        SearchNeedle query,
        EncodingLayout layout,
        int maxResults)
    {
        if (offset + query.Bytes.Length > bytesRead ||
            !MatchAt(buffer, offset, query.Bytes, query.LowerBytes, query.CaseSensitive))
            return false;

        var absOffset = bufferStart + offset;
        var column = (int)((absOffset - lineStart) / layout.Stride);
        results.Add(new SearchMatch(currentLine, column));
        return results.Count >= maxResults;
    }

    private static bool MatchAt(byte[] haystack, int offset, byte[] needle, byte[]? needleLower, bool caseSensitive)
    {
        return caseSensitive
            ? MatchExact(haystack, offset, needle)
            : MatchAsciiOrdinalIgnoreCase(haystack, offset, needleLower!);
    }

    private static bool MatchExact(byte[] haystack, int offset, byte[] needle)
    {
        for (int i = 0; i < needle.Length; i++)
            if (haystack[offset + i] != needle[i]) return false;
        return true;
    }

    private static bool MatchAsciiOrdinalIgnoreCase(byte[] haystack, int offset, byte[] needleLower)
    {
        for (int i = 0; i < needleLower.Length; i++)
        {
            var h = haystack[offset + i];
            if (h >= 0x41 && h <= 0x5A) h = (byte)(h + 32);
            if (h != needleLower[i]) return false;
        }

        return true;
    }

    private readonly record struct EncodingLayout(bool IsUtf16Le, bool IsUtf16Be, int Stride)
    {
        public static EncodingLayout From(Encoding encoding)
        {
            var isUtf16Le = encoding.Equals(Encoding.Unicode);
            var isUtf16Be = encoding.Equals(Encoding.BigEndianUnicode);
            return new EncodingLayout(isUtf16Le, isUtf16Be, isUtf16Le || isUtf16Be ? 2 : 1);
        }

        public bool IsNewline(byte[] buffer, int index, int length)
        {
            if (IsUtf16Le)
                return index + 1 < length && buffer[index] == 0x0A && buffer[index + 1] == 0x00;

            if (IsUtf16Be)
                return index + 1 < length && buffer[index] == 0x00 && buffer[index + 1] == 0x0A;

            return buffer[index] == 0x0A;
        }

        public bool IsNewline(MemoryMappedViewAccessor accessor, long offset, long fileSize)
        {
            var first = accessor.ReadByte(offset);

            if (IsUtf16Le)
                return first == 0x0A && ReadSecondByte(accessor, offset, fileSize) == 0x00;

            if (IsUtf16Be)
                return first == 0x00 && ReadSecondByte(accessor, offset, fileSize) == 0x0A;

            return first == 0x0A;
        }

        private static byte ReadSecondByte(MemoryMappedViewAccessor accessor, long offset, long fileSize) =>
            offset + 1 < fileSize ? accessor.ReadByte(offset + 1) : (byte)0;
    }

    private readonly record struct SearchNeedle(byte[] Bytes, byte[]? LowerBytes, bool CaseSensitive)
    {
        public static SearchNeedle Create(string needle, bool caseSensitive, Encoding encoding)
        {
            var bytes = encoding.GetBytes(needle);
            var lowerBytes = caseSensitive ? null : encoding.GetBytes(needle.ToLowerInvariant());
            return new SearchNeedle(bytes, lowerBytes, caseSensitive);
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
