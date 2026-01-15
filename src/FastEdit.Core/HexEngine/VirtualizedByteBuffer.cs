using System.IO.MemoryMappedFiles;

namespace FastEdit.Core.HexEngine;

public class VirtualizedByteBuffer : IDisposable
{
    private readonly string _filePath;
    private readonly FileStream _fileStream;
    private readonly MemoryMappedFile? _memoryMappedFile;
    private readonly long _fileLength;
    private readonly bool _useMemoryMapping;
    private readonly LruCache<long, byte[]> _pageCache;
    private readonly Dictionary<long, byte> _modifications = new();
    private bool _disposed;

    private const int PageSize = 64 * 1024;
    private const int MaxCachedPages = 16;

    public long Length => _fileLength;
    public bool HasModifications => _modifications.Count > 0;
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
        if (_modifications.TryGetValue(offset, out var modifiedByte))
            return modifiedByte;

        var bytes = GetBytes(offset, 1);
        return bytes.Length > 0 ? bytes[0] : (byte)0;
    }

    public bool IsModified(long offset)
    {
        return _modifications.ContainsKey(offset);
    }

    public void Save()
    {
        if (_modifications.Count == 0)
            return;

        // Close current streams
        _memoryMappedFile?.Dispose();
        _fileStream.Close();

        // Write modifications
        using (var writeStream = new FileStream(_filePath, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            foreach (var (offset, value) in _modifications.OrderBy(m => m.Key))
            {
                writeStream.Position = offset;
                writeStream.WriteByte(value);
            }
        }

        _modifications.Clear();
        _pageCache.Clear();
        ModificationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DiscardModifications()
    {
        _modifications.Clear();
        _pageCache.Clear();
        ModificationsChanged?.Invoke(this, EventArgs.Empty);
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
            _fileStream.Position = pageOffset;
            _ = _fileStream.Read(buffer, 0, bytesToRead);
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
