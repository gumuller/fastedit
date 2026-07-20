using System.IO.MemoryMappedFiles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using FastEdit.Core.Search;

namespace FastEdit.Core.HexEngine;

internal enum BinarySaveStage
{
    SnapshotProgress,
    BeforeCommit,
    BeforeReopen
}

public class VirtualizedByteBuffer : IDisposable
{
    private readonly string _filePath;
    private string _backingPath;
    private bool _deleteBackingOnDispose;
    private FileStream _fileStream;
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
    private readonly Action<BinarySaveStage, long>? _saveProgress;

    private const int PageSize = 64 * 1024;
    private const int MaxCachedPages = 16;
    private const int SearchChunkSize = 64 * 1024;

    /// <summary>
    /// Default ceiling for exhaustive hex-search offsets (80 KB of raw offset storage).
    /// </summary>
    public const int DefaultSearchResultLimit = 10_000;

    public long Length => _fileLength;
    public bool IsSnapshotBacked => _deleteBackingOnDispose;
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
        : this(filePath, ownsBackingFile: false, saveProgress: null)
    {
    }

    internal VirtualizedByteBuffer(
        string filePath,
        Action<BinarySaveStage, long>? saveProgress)
        : this(filePath, ownsBackingFile: false, saveProgress)
    {
    }

    private VirtualizedByteBuffer(
        string filePath,
        bool ownsBackingFile,
        Action<BinarySaveStage, long>? saveProgress)
    {
        _filePath = filePath;
        _backingPath = filePath;
        _deleteBackingOnDispose = ownsBackingFile;
        _saveProgress = saveProgress;
        _fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: PageSize,
            FileOptions.RandomAccess |
            FileOptions.Asynchronous |
            (ownsBackingFile ? FileOptions.DeleteOnClose : FileOptions.None));

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

    public static VirtualizedByteBuffer FromSnapshot(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var path = Path.Combine(
            Path.GetTempPath(),
            $"fastedit-binary-{Guid.NewGuid():N}.snapshot");
        File.WriteAllBytes(path, bytes);
        try
        {
            return new VirtualizedByteBuffer(
                path,
                ownsBackingFile: true,
                saveProgress: null);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    public byte[] CreateSnapshot()
    {
        if (_fileLength > int.MaxValue)
            throw new InvalidOperationException(
                "Binary session snapshots larger than 2 GB are not supported.");

        var snapshot = new byte[checked((int)_fileLength)];
        const int chunkSize = 1024 * 1024;
        for (long offset = 0; offset < _fileLength; offset += chunkSize)
        {
            var bytes = GetBytes(offset, (int)Math.Min(chunkSize, _fileLength - offset));
            bytes.CopyTo(snapshot.AsSpan(checked((int)offset)));
        }

        lock (_modificationsLock)
        {
            foreach (var modification in _modifications)
                snapshot[checked((int)modification.Key)] = modification.Value;
        }

        return snapshot;
    }

    public IReadOnlyList<KeyValuePair<long, byte>> GetModifications()
    {
        lock (_modificationsLock)
            return _modifications.OrderBy(modification => modification.Key).ToArray();
    }

    public string ComputeBaseSha256()
    {
        using var stream = new FileStream(
            _backingPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: PageSize,
            FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public bool MatchesBaseIdentity(long expectedLength, string expectedSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
        if (_fileLength != expectedLength)
            return false;

        lock (_streamLock)
        {
            _fileStream.Position = 0;
            var actualHash = Convert.ToHexString(SHA256.HashData(_fileStream));
            _fileStream.Position = 0;
            return string.Equals(actualHash, expectedSha256, StringComparison.Ordinal);
        }
    }

    public bool IsBackedBy(string filePath)
    {
        if (_deleteBackingOnDispose)
            return false;

        try
        {
            using var candidate = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            lock (_streamLock)
            {
                return TryGetFileIdentity(
                        _fileStream.SafeFileHandle,
                        out var currentIdentity) &&
                    TryGetFileIdentity(
                        candidate.SafeFileHandle,
                        out var candidateIdentity) &&
                    currentIdentity == candidateIdentity;
            }
        }
        catch (IOException)
        {
            return HasExactNormalizedPath(filePath);
        }
        catch (UnauthorizedAccessException)
        {
            return HasExactNormalizedPath(filePath);
        }
    }

    public void SaveTo(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (IsBackedBy(filePath))
        {
            Save();
            return;
        }
        if (HasExactNormalizedPath(filePath))
        {
            throw new IOException(
                "The binary source was replaced after it was opened.");
        }

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(
            directory ?? string.Empty,
            $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        var modifications = GetModifications();
        var buffer = new byte[1024 * 1024];
        try
        {
            var securitySource = File.Exists(filePath)
                ? filePath
                : _backingPath;
            CreateProtectedSibling(securitySource, tempPath);
            using (var output = new FileStream(
                tempPath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.None,
                buffer.Length,
                FileOptions.WriteThrough))
            {
                for (long offset = 0; offset < _fileLength;)
                {
                    var count = (int)Math.Min(buffer.Length, _fileLength - offset);
                    var read = RandomAccess.Read(
                        _fileStream.SafeFileHandle,
                        buffer.AsSpan(0, count),
                        offset);
                    if (read == 0)
                        throw new EndOfStreamException("The binary source ended before its expected length.");

                    ApplyModifications(buffer, read, offset, modifications);
                    output.Write(buffer, 0, read);
                    offset += read;
                }

                output.Flush(flushToDisk: true);
            }

            if (File.Exists(filePath))
            {
                var backupPath = Path.Combine(
                    directory ?? string.Empty,
                    $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.backup");
                var replaced = false;
                try
                {
                    EnsureReplaceableFile(filePath);
                    File.Replace(
                        tempPath,
                        filePath,
                        backupPath,
                        ignoreMetadataErrors: false);
                    replaced = true;
                }
                finally
                {
                    if (replaced && File.Exists(backupPath))
                        File.Delete(backupPath);
                }
            }
            else
            {
                File.Move(tempPath, filePath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
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
        KeyValuePair<long, byte>[] modifications;
        lock (_modificationsLock)
        {
            if (_modifications.Count == 0)
                return;

            modifications = _modifications.OrderBy(m => m.Key).ToArray();
        }

        var activeSearches = BeginSave();
        var directory = Path.GetDirectoryName(_filePath) ?? string.Empty;
        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
        var basePath = Path.Combine(
            directory,
            $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.base");
        var backupPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.backup");
        var rollbackPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.rollback");
        var backingReady = true;
        var baseAdopted = false;
        var deleteBackup = false;
        try
        {
            Task.WaitAll(activeSearches.Select(search => search.Completion.Task).ToArray());
            if ((File.GetAttributes(_filePath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "Saving through a reparse-point binary path is not supported.");
            }
            if (!TryGetFileIdentity(
                    _fileStream.SafeFileHandle,
                    out var originalIdentity) ||
                originalIdentity.NumberOfLinks != 1)
            {
                throw new IOException(
                    "The binary source identity cannot be replaced safely.");
            }
            EnsureReplaceableFile(_filePath);
            if (!TryGetPathIdentity(_filePath, out var openedPathIdentity) ||
                !HasSameFileId(openedPathIdentity, originalIdentity))
            {
                throw new IOException(
                    "The binary source was replaced after it was opened.");
            }

            var buffer = new byte[1024 * 1024];
            CreateProtectedSibling(_filePath, tempPath);
            CreateProtectedSibling(_filePath, basePath);
            using (var output = new FileStream(
                tempPath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.None,
                buffer.Length,
                FileOptions.WriteThrough))
            using (var baseOutput = new FileStream(
                basePath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.None,
                buffer.Length,
                FileOptions.WriteThrough))
            {
                for (long offset = 0; offset < _fileLength;)
                {
                    var count = (int)Math.Min(buffer.Length, _fileLength - offset);
                    var read = RandomAccess.Read(
                        _fileStream.SafeFileHandle,
                        buffer.AsSpan(0, count),
                        offset);
                    if (read == 0)
                        throw new EndOfStreamException(
                            "The binary source ended before its expected length.");

                    baseOutput.Write(buffer, 0, read);
                    ApplyModifications(buffer, read, offset, modifications);
                    output.Write(buffer, 0, read);
                    offset += read;
                    _saveProgress?.Invoke(
                        BinarySaveStage.SnapshotProgress,
                        offset);
                }

                output.Flush(flushToDisk: true);
                baseOutput.Flush(flushToDisk: true);
            }

            using (var candidate = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                if (!TryGetFileIdentity(
                        candidate.SafeFileHandle,
                        out var currentIdentity) ||
                    !HasSameFileId(originalIdentity, currentIdentity) ||
                    currentIdentity.NumberOfLinks != 1)
                {
                    throw new IOException(
                        "The binary source changed while the save snapshot was being prepared.");
                }
            }

            _memoryMappedFile?.Dispose();
            _memoryMappedFile = null;
            _fileStream.Dispose();
            backingReady = false;

            _saveProgress?.Invoke(BinarySaveStage.BeforeCommit, _fileLength);
            var committed = false;
            try
            {
                File.Replace(
                    tempPath,
                    _filePath,
                    backupPath,
                    ignoreMetadataErrors: false);
                committed = true;

                if (!TryGetPathIdentity(backupPath, out var replacedIdentity) ||
                    !HasSameFileId(replacedIdentity, originalIdentity) ||
                    !FilesHaveSameContent(basePath, backupPath))
                {
                    throw new IOException(
                        "The binary source changed immediately before the save commit.");
                }
                _saveProgress?.Invoke(
                    BinarySaveStage.BeforeReopen,
                    _fileLength);
                if (!TryOpenBackingResources(
                        _filePath,
                        out var committedStream,
                        out var committedMap))
                {
                    throw new IOException(
                        "The committed binary source could not be reopened.");
                }

                _fileStream = committedStream;
                _memoryMappedFile = committedMap;
                _backingPath = _filePath;
                _deleteBackingOnDispose = false;
                backingReady = true;
            }
            catch
            {
                if (committed && File.Exists(backupPath))
                {
                    try
                    {
                        File.Replace(
                            backupPath,
                            _filePath,
                            rollbackPath,
                            ignoreMetadataErrors: false);
                    }
                    catch
                    {
                        // The backup is intentionally retained if rollback fails.
                    }
                }
                throw;
            }

            deleteBackup = true;

            lock (_modificationsLock)
                _modifications.Clear();
            _pageCache.Clear();
        }
        finally
        {
            if (!backingReady)
            {
                if (!TryOpenBackingResources(
                        basePath,
                        out var recoveredStream,
                        out var recoveredMap))
                {
                    EndSave();
                    throw new IOException(
                        "The binary save failed and its protected base snapshot could not be reopened.");
                }

                _fileStream = recoveredStream;
                _memoryMappedFile = recoveredMap;
                _backingPath = basePath;
                _deleteBackingOnDispose = true;
                baseAdopted = true;
            }
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            if (!baseAdopted && File.Exists(basePath))
                File.Delete(basePath);
            if (File.Exists(rollbackPath))
                File.Delete(rollbackPath);
            if (deleteBackup && File.Exists(backupPath))
                File.Delete(backupPath);
            EndSave();
        }

        ModificationsChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool HasExactNormalizedPath(string filePath)
    {
        return string.Equals(
            Path.GetFullPath(filePath),
            Path.GetFullPath(_filePath),
            StringComparison.Ordinal);
    }

    private static bool TryGetPathIdentity(
        string filePath,
        out FileIdentity identity)
    {
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return TryGetFileIdentity(stream.SafeFileHandle, out identity);
        }
        catch (IOException)
        {
            identity = default;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            identity = default;
            return false;
        }
    }

    private bool TryOpenBackingResources(
        string filePath,
        out FileStream stream,
        out MemoryMappedFile? memoryMap)
    {
        stream = null!;
        memoryMap = null;
        try
        {
            stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: PageSize,
                FileOptions.RandomAccess | FileOptions.Asynchronous);
            if (stream.Length > 1024 * 1024)
            {
                memoryMap = MemoryMappedFile.CreateFromFile(
                    stream,
                    mapName: null,
                    capacity: 0,
                    MemoryMappedFileAccess.Read,
                    HandleInheritability.None,
                    leaveOpen: true);
            }
            return true;
        }
        catch (IOException)
        {
            memoryMap?.Dispose();
            stream?.Dispose();
            stream = null!;
            memoryMap = null;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            memoryMap?.Dispose();
            stream?.Dispose();
            stream = null!;
            memoryMap = null;
            return false;
        }
    }

    private static bool HasSameFileId(
        FileIdentity first,
        FileIdentity second)
    {
        return first.VolumeSerialNumber == second.VolumeSerialNumber &&
            first.FileIndexHigh == second.FileIndexHigh &&
            first.FileIndexLow == second.FileIndexLow;
    }

    private static bool FilesHaveSameContent(
        string firstPath,
        string secondPath)
    {
        using var first = new FileStream(
            firstPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var second = new FileStream(
            secondPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return first.Length == second.Length &&
            SHA256.HashData(first).AsSpan().SequenceEqual(
                SHA256.HashData(second));
    }

    private static void CreateProtectedSibling(
        string securitySource,
        string siblingPath)
    {
        using (new FileStream(
            siblingPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None))
        {
        }

        if (OperatingSystem.IsWindows())
        {
            CopyDiscretionaryAccessControl(
                securitySource,
                siblingPath);
        }
        else
        {
            File.SetUnixFileMode(
                siblingPath,
                File.GetUnixFileMode(securitySource));
        }
    }

    private static void EnsureReplaceableFile(string filePath)
    {
        if ((File.GetAttributes(filePath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Replacing a reparse-point binary path is not supported.");

        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (!TryGetFileIdentity(stream.SafeFileHandle, out var identity) ||
            identity.NumberOfLinks != 1)
        {
            throw new IOException(
                "Replacing a hard-linked binary file is not supported.");
        }
        if (OperatingSystem.IsWindows() && HasAlternateDataStreams(filePath))
        {
            throw new IOException(
                "Replacing a binary file with alternate data streams is not supported.");
        }
    }

    private static void CopyDiscretionaryAccessControl(
        string sourcePath,
        string destinationPath)
    {
        _ = GetFileSecurity(
            sourcePath,
            DaclSecurityInformation,
            null,
            0,
            out var requiredLength);
        var error = Marshal.GetLastWin32Error();
        if (requiredLength == 0 || error != ErrorInsufficientBuffer)
            throw new Win32Exception(error);

        var descriptor = new byte[requiredLength];
        if (!GetFileSecurity(
                sourcePath,
                DaclSecurityInformation,
                descriptor,
                requiredLength,
                out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        if (!SetFileSecurity(
                destinationPath,
                DaclSecurityInformation,
                descriptor))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static bool HasAlternateDataStreams(string filePath)
    {
        var findHandle = FindFirstStream(
            filePath,
            StreamInfoLevels.FindStreamInfoStandard,
            out var streamData,
            0);
        if (findHandle == InvalidHandleValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            do
            {
                if (!string.Equals(
                        streamData.StreamName,
                        "::$DATA",
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            while (FindNextStream(findHandle, out streamData));

            var error = Marshal.GetLastWin32Error();
            if (error != ErrorHandleEof)
                throw new Win32Exception(error);
            return false;
        }
        finally
        {
            _ = FindClose(findHandle);
        }
    }

    private static bool TryGetFileIdentity(
        SafeFileHandle handle,
        out FileIdentity identity)
    {
        if (OperatingSystem.IsWindows() &&
            GetFileInformationByHandle(handle, out var information))
        {
            identity = new FileIdentity(
                information.VolumeSerialNumber,
                information.FileIndexHigh,
                information.FileIndexLow,
                information.NumberOfLinks);
            return true;
        }

        identity = default;
        return false;
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
            _backingPath,
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
        if (_deleteBackingOnDispose)
        {
            try
            {
                File.Delete(_backingPath);
            }
            catch (IOException)
            {
                // Process-owned snapshot cleanup is best effort during disposal.
            }
            catch (UnauthorizedAccessException)
            {
                // Process-owned snapshot cleanup is best effort during disposal.
            }
        }

        GC.SuppressFinalize(this);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out ByHandleFileInformation lpFileInformation);

    [DllImport(
        "advapi32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileSecurity(
        string lpFileName,
        uint requestedInformation,
        byte[]? securityDescriptor,
        uint length,
        out uint lengthNeeded);

    [DllImport(
        "advapi32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileSecurity(
        string lpFileName,
        uint securityInformation,
        byte[] securityDescriptor);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        EntryPoint = "FindFirstStreamW",
        ExactSpelling = true,
        SetLastError = true)]
    private static extern IntPtr FindFirstStream(
        string lpFileName,
        StreamInfoLevels infoLevel,
        out Win32FindStreamData findStreamData,
        uint flags);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "FindNextStreamW",
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextStream(
        IntPtr findStream,
        out Win32FindStreamData findStreamData);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr findFile);

    private const uint DaclSecurityInformation = 0x00000004;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorHandleEof = 38;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    private enum StreamInfoLevels
    {
        FindStreamInfoStandard
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Win32FindStreamData
    {
        public long StreamSize;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)]
        public string StreamName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    private readonly record struct FileIdentity(
        uint VolumeSerialNumber,
        uint FileIndexHigh,
        uint FileIndexLow,
        uint NumberOfLinks);

    private sealed class ActiveSearch(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
