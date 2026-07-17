using System.IO.MemoryMappedFiles;
using FastEdit.Core.Search;

namespace FastEdit.Core.HexEngine;

public class VirtualizedByteBuffer : IDisposable
{
    private readonly string _filePath;
    private FileStream _fileStream;
    private MemoryMappedFile? _memoryMappedFile;
    private long _fileLength;
    private bool _useMemoryMapping;
    private readonly LruCache<long, byte[]> _pageCache;
    private readonly Dictionary<long, byte> _modifications = new();
    private readonly object _modificationsLock = new();
    private readonly object _streamLock = new();
    private readonly object _searchSaveLock = new();
    private bool _disposed;

    private const int PageSize = 64 * 1024;
    private const int MaxCachedPages = 16;
    private const int SearchChunkSize = 64 * 1024;

    /// <summary>
    /// Default ceiling for exhaustive hex-search offsets (80 KB of raw offset storage).
    /// </summary>
    public const int DefaultSearchResultLimit = 10_000;

    public long Length => _fileLength;
    public bool HasModifications
    {
        get
        {
            lock (_modificationsLock)
                return _modifications.Count > 0;
        }
    }
    public event EventHandler? ModificationsChanged;

    public VirtualizedByteBuffer(string filePath)
    {
        _filePath = filePath;
        _fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: PageSize,
            FileOptions.RandomAccess | FileOptions.Asynchronous);

        _fileLength = _fileStream.Length;
        _pageCache = new LruCache<long, byte[]>(MaxCachedPages);

        _useMemoryMapping = _fileLength > 1024 * 1024;

        if (_useMemoryMapping)
        {
            _memoryMappedFile = MemoryMappedFile.CreateFromFile(
                _fileStream,
                mapName: null,
                capacity: 0,
                MemoryMappedFileAccess.Read,
                HandleInheritability.None,
                leaveOpen: true);
        }
    }

    public void SetByte(long offset, byte value)
    {
        if (offset < 0 || offset >= _fileLength)
            return;

        lock (_modificationsLock)
            _modifications[offset] = value;

        // Update cached page if present
        long pageNumber = offset / PageSize;
        if (_pageCache.TryGet(pageNumber, out var page))
        {
            int pageOffset = (int)(offset % PageSize);
            if (pageOffset < page.Length)
            {
                page[pageOffset] = value;
            }
        }

        ModificationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public byte GetByte(long offset)
    {
        lock (_modificationsLock)
        {
            if (_modifications.TryGetValue(offset, out var modifiedByte))
                return modifiedByte;
        }

        var bytes = GetBytes(offset, 1);
        return bytes.Length > 0 ? bytes[0] : (byte)0;
    }

    public bool IsModified(long offset)
    {
        lock (_modificationsLock)
            return _modifications.ContainsKey(offset);
    }

    public void Save()
    {
        lock (_searchSaveLock)
            SaveCore();
    }

    private void SaveCore()
    {
        KeyValuePair<long, byte>[] modifications;
        lock (_modificationsLock)
        {
            if (_modifications.Count == 0)
                return;

            modifications = _modifications.OrderBy(m => m.Key).ToArray();
        }

        // Write modifications to a temporary approach: close current handles,
        // write changes, then reopen. Use local variables to ensure failure safety.
        _memoryMappedFile?.Dispose();
        _fileStream.Close();

        try
        {
            using (var writeStream = new FileStream(_filePath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                foreach (var (offset, value) in modifications)
                {
                    writeStream.Position = offset;
                    writeStream.WriteByte(value);
                }
            }

            lock (_modificationsLock)
                _modifications.Clear();
            _pageCache.Clear();
        }
        finally
        {
            // Always reopen the file stream and memory map so the instance remains usable
            var newStream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: PageSize,
                FileOptions.RandomAccess | FileOptions.Asynchronous);

            _fileStream = newStream;
            _fileLength = _fileStream.Length;

            _useMemoryMapping = _fileLength > 1024 * 1024;
            if (_useMemoryMapping)
            {
                _memoryMappedFile = MemoryMappedFile.CreateFromFile(
                    _fileStream,
                    mapName: null,
                    capacity: 0,
                    MemoryMappedFileAccess.Read,
                    HandleInheritability.None,
                    leaveOpen: true);
            }
            else
            {
                _memoryMappedFile = null;
            }
        }

        ModificationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DiscardModifications()
    {
        lock (_modificationsLock)
            _modifications.Clear();
        _pageCache.Clear();
        ModificationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<BoundedSearchResult<long>> SearchAsync(
        ReadOnlyMemory<byte> pattern,
        int maxResults,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pattern.IsEmpty)
            return new BoundedSearchResult<long>(Array.Empty<long>(), false);
        if (maxResults <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxResults));

        var patternBytes = pattern.ToArray();
        return await Task.Run(
                () => Search(patternBytes, maxResults, progress, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private BoundedSearchResult<long> Search(
        byte[] pattern,
        int maxResults,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        lock (_searchSaveLock)
            return SearchCore(pattern, maxResults, progress, cancellationToken);
    }

    private BoundedSearchResult<long> SearchCore(
        byte[] pattern,
        int maxResults,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pattern.LongLength > _fileLength)
            return new BoundedSearchResult<long>(Array.Empty<long>(), false);

        KeyValuePair<long, byte>[] modifications;
        lock (_modificationsLock)
            modifications = _modifications.OrderBy(pair => pair.Key).ToArray();

        var results = new List<long>(Math.Min(maxResults, DefaultSearchResultLimit));
        var overlap = pattern.Length - 1;
        var buffer = new byte[checked(SearchChunkSize + overlap)];
        long position = 0;
        using var searchStream = new FileStream(
            _filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: SearchChunkSize,
            FileOptions.RandomAccess | FileOptions.Asynchronous);

        while (position <= _fileLength - pattern.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesToRead = (int)Math.Min(buffer.Length, _fileLength - position);
            ReadSearchChunk(searchStream, position, buffer, bytesToRead);
            ApplyModifications(buffer, bytesToRead, position, modifications);

            var finalChunk = position + bytesToRead >= _fileLength;
            var candidates = finalChunk
                ? bytesToRead - pattern.Length + 1
                : Math.Min(SearchChunkSize, bytesToRead - pattern.Length + 1);

            for (var offset = 0; offset < candidates; offset++)
            {
                if ((offset & 0xFFF) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                if (!buffer.AsSpan(offset, pattern.Length).SequenceEqual(pattern))
                    continue;

                if (results.Count >= maxResults)
                    return new BoundedSearchResult<long>(results, true);

                results.Add(position + offset);
            }

            position += candidates;
            progress?.Report(Math.Min(1.0, (double)position / _fileLength));
        }

        progress?.Report(1.0);
        return new BoundedSearchResult<long>(results, false);
    }

    private void ReadSearchChunk(
        FileStream searchStream,
        long position,
        byte[] buffer,
        int bytesToRead)
    {
        var totalRead = 0;
        while (totalRead < bytesToRead)
        {
            var read = RandomAccess.Read(
                searchStream.SafeFileHandle,
                buffer.AsSpan(totalRead, bytesToRead - totalRead),
                position + totalRead);
            if (read == 0)
                break;
            totalRead += read;
        }
    }

    private static void ApplyModifications(
        byte[] buffer,
        int bytesRead,
        long position,
        IReadOnlyList<KeyValuePair<long, byte>> modifications)
    {
        var end = position + bytesRead;
        foreach (var modification in modifications)
        {
            if (modification.Key < position)
                continue;
            if (modification.Key >= end)
                break;

            buffer[(int)(modification.Key - position)] = modification.Value;
        }
    }

    public ReadOnlySpan<byte> GetBytes(long offset, int count)
    {
        if (offset < 0 || offset >= _fileLength)
            return ReadOnlySpan<byte>.Empty;

        count = (int)Math.Min(count, _fileLength - offset);

        long startPage = offset / PageSize;
        long endPage = (offset + count - 1) / PageSize;

        if (startPage == endPage)
        {
            var page = GetOrLoadPage(startPage);
            int pageOffset = (int)(offset % PageSize);
            return page.AsSpan(pageOffset, count);
        }

        var result = new byte[count];
        int resultOffset = 0;

        for (long pageNum = startPage; pageNum <= endPage; pageNum++)
        {
            var page = GetOrLoadPage(pageNum);
            int pageStart = pageNum == startPage ? (int)(offset % PageSize) : 0;
            int pageEnd = pageNum == endPage
                ? (int)((offset + count - 1) % PageSize) + 1
                : PageSize;
            int bytesToCopy = Math.Min(pageEnd - pageStart, page.Length - pageStart);

            if (bytesToCopy > 0)
            {
                Array.Copy(page, pageStart, result, resultOffset, bytesToCopy);
                resultOffset += bytesToCopy;
            }
        }

        return result;
    }

    private byte[] GetOrLoadPage(long pageNumber)
    {
        if (_pageCache.TryGet(pageNumber, out var cachedPage))
            return cachedPage;

        var page = LoadPage(pageNumber);
        _pageCache.Add(pageNumber, page);
        return page;
    }

    private byte[] LoadPage(long pageNumber)
    {
        long pageOffset = pageNumber * PageSize;
        int bytesToRead = (int)Math.Min(PageSize, _fileLength - pageOffset);
        var buffer = new byte[bytesToRead];

        if (_useMemoryMapping && _memoryMappedFile != null)
        {
            using var accessor = _memoryMappedFile.CreateViewAccessor(
                pageOffset, bytesToRead, MemoryMappedFileAccess.Read);
            accessor.ReadArray(0, buffer, 0, bytesToRead);
        }
        else
        {
            lock (_streamLock)
            {
                _fileStream.Position = pageOffset;
                _ = _fileStream.Read(buffer, 0, bytesToRead);
            }
        }

        return buffer;
    }

    public IEnumerable<HexRowData> GetRows(long startOffset, int rowCount, int bytesPerRow)
    {
        startOffset = (startOffset / bytesPerRow) * bytesPerRow;

        for (int i = 0; i < rowCount; i++)
        {
            long rowOffset = startOffset + (i * bytesPerRow);
            if (rowOffset >= _fileLength)
                yield break;

            var bytes = GetBytes(rowOffset, bytesPerRow);
            yield return new HexRowData(rowOffset, bytes.ToArray(), bytesPerRow);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _memoryMappedFile?.Dispose();
        _fileStream.Dispose();
        _pageCache.Clear();

        GC.SuppressFinalize(this);
    }
}
