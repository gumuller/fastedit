using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using FastEdit.Core.Search;

namespace FastEdit.Core.HexEngine;

public class VirtualizedByteBuffer : IDisposable
{
    private readonly string _filePath;
    private string _backingPath;
    private bool _ownsBackingPath;
    private readonly Action<Stream>? _saveSnapshotWriter;
    private readonly Action? _beforeAtomicCommit;
    private FileStream _fileStream;
    private BackingFileIdentity _backingIdentity;
    private MemoryMappedFile? _memoryMappedFile;
    private long _fileLength;
    private bool _useMemoryMapping;
    private readonly LruCache<long, byte[]> _pageCache;
    private readonly Dictionary<long, byte> _modifications = new();
    private readonly object _modificationsLock = new();
    private readonly object _streamLock = new();
    private readonly object _searchLifecycleLock = new();
    private readonly HashSet<ActiveSearch> _activeSearches = new();
    private bool _saveInProgress;
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
        : this(filePath, null)
    {
    }

    internal VirtualizedByteBuffer(
        string filePath,
        Action<Stream>? saveSnapshotWriter,
        Action? beforeAtomicCommit = null)
    {
        _filePath = filePath;
        _backingPath = filePath;
        _saveSnapshotWriter = saveSnapshotWriter;
        _beforeAtomicCommit = beforeAtomicCommit;
        _fileStream = OpenBackingStream();

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
        _backingIdentity = CaptureBackingIdentity(_fileStream, _backingPath);
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

    public void WriteSnapshot(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_modificationsLock)
        {
            const int chunkSize = 64 * 1024;
            var modifications = _modifications.OrderBy(item => item.Key).ToArray();
            var modificationIndex = 0;
            for (long offset = 0; offset < _fileLength; offset += chunkSize)
            {
                var count = (int)Math.Min(chunkSize, _fileLength - offset);
                var chunk = GetBytes(offset, count).ToArray();
                while (modificationIndex < modifications.Length &&
                       modifications[modificationIndex].Key < offset + count)
                {
                    var modification = modifications[modificationIndex++];
                    chunk[modification.Key - offset] = modification.Value;
                }

                destination.Write(chunk);
            }
        }
    }

    public void Save()
    {
        lock (_modificationsLock)
        {
            if (_modifications.Count == 0)
                return;
        }

        var activeSearches = BeginSave();
        try
        {
            Task.WaitAll(activeSearches.Select(search => search.Completion.Task).ToArray());

            lock (_modificationsLock)
            {
                if (_modifications.Count == 0)
                    return;

                var directory = Path.GetDirectoryName(_filePath) ??
                    throw new InvalidOperationException(
                        "The binary file does not have a parent directory.");
                var tempPath = Path.Combine(
                    directory,
                    $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
                try
                {
                    using (var writeStream = new FileStream(
                               tempPath,
                               FileMode.CreateNew,
                               FileAccess.Write,
                               FileShare.None))
                    {
                        (_saveSnapshotWriter ?? WriteSnapshot)(writeStream);
                        writeStream.Flush(flushToDisk: true);
                    }

                    var expectedBacking = CaptureBackingFingerprint(
                        _fileStream,
                        _backingPath);
                    var replacementIdentity =
                        CapturePathIdentity(tempPath);
                    _beforeAtomicCommit?.Invoke();
                    CommitAtomicReplacement(
                        tempPath,
                        expectedBacking,
                        replacementIdentity);
                    try
                    {
                        _modifications.Clear();
                        _pageCache.Clear();
                    }
                    finally
                    {
                        DisposeBackingHandles();
                        ReopenBackingHandles();
                    }
                }
                finally
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
            }
        }
        finally
        {
            EndSave();
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
        if (pattern.IsEmpty)
            return new BoundedSearchResult<long>(Array.Empty<long>(), false);
        if (maxResults <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxResults));

        var activeSearch = BeginSearch(cancellationToken);
        var patternBytes = pattern.ToArray();
        try
        {
            return await Task.Run(
                    () => Search(patternBytes, maxResults, progress, activeSearch.Cancellation.Token),
                    activeSearch.Cancellation.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            EndSearch(activeSearch);
        }
    }

    private ActiveSearch BeginSearch(CancellationToken cancellationToken)
    {
        var activeSearch = new ActiveSearch(
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
        var registered = false;

        try
        {
            lock (_searchLifecycleLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_saveInProgress)
                    activeSearch.Cancellation.Cancel();
                else
                {
                    _activeSearches.Add(activeSearch);
                    registered = true;
                }
            }

            activeSearch.Cancellation.Token.ThrowIfCancellationRequested();
            return activeSearch;
        }
        catch
        {
            if (registered)
                EndSearch(activeSearch);
            else
                activeSearch.Cancellation.Dispose();
            throw;
        }
    }

    private void EndSearch(ActiveSearch activeSearch)
    {
        lock (_searchLifecycleLock)
            _activeSearches.Remove(activeSearch);

        activeSearch.Completion.TrySetResult();
        activeSearch.Cancellation.Dispose();
    }

    private ActiveSearch[] BeginSave()
    {
        lock (_searchLifecycleLock)
        {
            _saveInProgress = true;
            var activeSearches = _activeSearches.ToArray();
            foreach (var activeSearch in activeSearches)
                activeSearch.Cancellation.Cancel();
            return activeSearches;
        }
    }

    private void EndSave()
    {
        lock (_searchLifecycleLock)
            _saveInProgress = false;
    }

    private FileStream OpenBackingStream(bool allowDelete = true) =>
        new(
            _backingPath,
            FileMode.Open,
            FileAccess.Read,
            allowDelete
                ? FileShare.Read | FileShare.Delete
                : FileShare.Read,
            bufferSize: PageSize,
            FileOptions.RandomAccess | FileOptions.Asynchronous);

    private void DisposeBackingHandles()
    {
        _memoryMappedFile?.Dispose();
        _memoryMappedFile = null;
        _fileStream.Dispose();
    }

    private void ReopenBackingHandles(bool allowDelete = true)
    {
        _fileStream = OpenBackingStream(allowDelete);
        _fileLength = _fileStream.Length;
        _backingIdentity = CaptureBackingIdentity(_fileStream, _backingPath);
        _useMemoryMapping = _fileLength > 1024 * 1024;
        _memoryMappedFile = _useMemoryMapping
            ? MemoryMappedFile.CreateFromFile(
                _fileStream,
                mapName: null,
                capacity: 0,
                MemoryMappedFileAccess.Read,
                HandleInheritability.None,
                leaveOpen: true)
            : null;
    }

    private void CommitAtomicReplacement(
        string tempPath,
        BackingFileFingerprint expectedBacking,
        BackingFileIdentity replacementIdentity)
    {
        var backupPath = Path.Combine(
            Path.GetDirectoryName(_filePath)!,
            $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.backup");
        var previousOwnedBackingPath =
            _ownsBackingPath ? _backingPath : null;
        var safetySnapshotPath = CreateSafetySnapshot(tempPath);
        var reopened = false;
        var handlesDisposed = false;
        var replacementCommitted = false;
        var commitSucceeded = false;
        try
        {
            ReplaceFileWithRetries(
                tempPath,
                _filePath,
                backupPath);
            replacementCommitted = true;
            DisposeBackingHandles();
            handlesDisposed = true;
            _backingPath = _filePath;
            _ownsBackingPath = false;
            ReopenBackingHandles(allowDelete: false);
            reopened = true;
            if (_backingIdentity != replacementIdentity)
            {
                DisposeBackingHandles();
                reopened = false;
                handlesDisposed = true;
                _backingPath = safetySnapshotPath;
                _ownsBackingPath = true;
                _pageCache.Clear();
                ReopenBackingHandles();
                reopened = true;
                DeleteOwnedBackingPath(previousOwnedBackingPath);
                throw new IOException(
                    "The binary file changed while the save was being committed.");
            }

            if (!MatchesBackingFingerprint(backupPath, expectedBacking))
            {
                DisposeBackingHandles();
                reopened = false;
                handlesDisposed = true;
                var displacedPath = Path.Combine(
                    Path.GetDirectoryName(_filePath)!,
                    $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.conflict");
                ReplaceFileWithRetries(
                    backupPath,
                    _filePath,
                    displacedPath);
                var displacedIsSnapshot = MatchesBackingIdentity(
                    displacedPath,
                    CapturePathIdentity(safetySnapshotPath));
                _backingPath = safetySnapshotPath;
                _ownsBackingPath = true;
                _pageCache.Clear();
                if (displacedIsSnapshot)
                    DeleteOwnedBackingPath(displacedPath);
                ReopenBackingHandles();
                reopened = true;
                DeleteOwnedBackingPath(previousOwnedBackingPath);
                throw new IOException(
                    "The binary file changed before the save could be committed.");
            }

            File.Delete(backupPath);
            DeleteOwnedBackingPath(previousOwnedBackingPath);
            commitSucceeded = true;
        }
        finally
        {
            if (!reopened && handlesDisposed)
                ReopenBackingHandles();
            if (!replacementCommitted || commitSucceeded)
                DeleteOwnedBackingPath(safetySnapshotPath);
        }
    }

    private BackingFileIdentity CapturePathIdentity(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return CaptureBackingIdentity(stream, path);
    }

    private static void DeleteOwnedBackingPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string CreateSafetySnapshot(string sourcePath)
    {
        var path = Path.Combine(
            Path.GetDirectoryName(_filePath)!,
            $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.snapshot");
        try
        {
            if (!OperatingSystem.IsWindows() ||
                !CreateHardLink(path, sourcePath, IntPtr.Zero))
            {
                File.Copy(sourcePath, path, overwrite: false);
            }
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException or
                PlatformNotSupportedException or
                NotSupportedException)
        {
            File.Copy(sourcePath, path, overwrite: false);
        }
        return path;
    }

    private BackingFileFingerprint CaptureBackingFingerprint(
        FileStream stream,
        string path)
    {
        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            return new BackingFileFingerprint(
                CaptureBackingIdentity(stream, path),
                stream.Length,
                Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(stream)));
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private bool MatchesBackingFingerprint(
        string path,
        BackingFileFingerprint expected)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return CaptureBackingFingerprint(stream, path) == expected;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static void ReplaceFileWithRetries(
        string sourcePath,
        string destinationPath,
        string backupPath)
    {
        const int maxAttempts = 10;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Replace(
                    sourcePath,
                    destinationPath,
                    backupPath,
                    ignoreMetadataErrors: true);
                return;
            }
            catch (Exception ex) when (
                attempt < maxAttempts - 1 &&
                File.Exists(sourcePath) &&
                ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(
                    10 * (attempt + 1)));
            }
        }
    }

    private BackingFileIdentity CaptureBackingIdentity(
        FileStream stream,
        string path)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!GetFileInformationByHandle(
                    stream.SafeFileHandle,
                    out var information))
            {
                throw new IOException(
                    "The binary backing-file identity could not be read.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            }

            return new BackingFileIdentity(
                information.VolumeSerialNumber,
                information.FileIndexHigh,
                information.FileIndexLow,
                0,
                default);
        }

        var fileInfo = new FileInfo(path);
        return new BackingFileIdentity(
            0,
            0,
            0,
            fileInfo.Length,
            fileInfo.CreationTimeUtc);
    }

    private bool MatchesBackingIdentity(
        string path,
        BackingFileIdentity expectedIdentity)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return CaptureBackingIdentity(stream, path) == expectedIdentity;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle fileHandle,
        out ByHandleFileInformation fileInformation);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(
        string fileName,
        string existingFileName,
        IntPtr securityAttributes);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    private readonly record struct BackingFileIdentity(
        uint VolumeSerialNumber,
        uint FileIndexHigh,
        uint FileIndexLow,
        long Length,
        DateTime CreationTimeUtc);

    private readonly record struct BackingFileFingerprint(
        BackingFileIdentity Identity,
        long Length,
        string ContentHash);

    private BoundedSearchResult<long> Search(
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
        if (_ownsBackingPath)
            DeleteOwnedBackingPath(_backingPath);

        GC.SuppressFinalize(this);
    }

    private sealed class ActiveSearch(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
