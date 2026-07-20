using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FastEdit.Services.Interfaces;
using Microsoft.Win32.SafeHandles;

namespace FastEdit.Services;

public class FileSystemService : IFileSystemService, ISecureFileSystemService
{
    private readonly Action<string, string, bool> _moveFile;
    private readonly Action<string, string, string> _replaceFile;
    private readonly Action<TimeSpan> _delay;

    public FileSystemService()
        : this(
            (source, destination, overwrite) =>
                File.Move(source, destination, overwrite),
            (source, destination, backup) =>
                File.Replace(
                    source,
                    destination,
                    backup,
                    ignoreMetadataErrors: true),
            Thread.Sleep)
    {
    }

    internal FileSystemService(
        Action<string, string, bool> moveFile,
        Action<string, string, string> replaceFile,
        Action<TimeSpan> delay)
    {
        _moveFile = moveFile;
        _replaceFile = replaceFile;
        _delay = delay;
    }

    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public string[] GetFiles(string path, string searchPattern = "*", bool recursive = false)
        => Directory.GetFiles(path, searchPattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
    public string[] GetDirectories(string path) => Directory.GetDirectories(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public string ReadAllText(string path, Encoding encoding) => File.ReadAllText(path, encoding);
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
    public Task<byte[]> ReadAllBytesAsync(string path) => File.ReadAllBytesAsync(path);
    public Task<string> ReadAllTextAsync(string path) => File.ReadAllTextAsync(path);
    public void WriteAllText(string path, string content) => File.WriteAllText(path, content);
    public void WriteAllText(string path, string content, Encoding encoding) => File.WriteAllText(path, content, encoding);
    public void WriteAllTextAtomic(string path, string content)
        => WriteStreamAtomic(path, stream =>
        {
            using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(false),
                leaveOpen: true);
            writer.Write(content);
            writer.Flush();
        });

    public void WriteStreamAtomic(string path, Action<Stream> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory ?? string.Empty, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            CommitAtomicReplacement(tempPath, path);
        }

        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private void CommitAtomicReplacement(string tempPath, string path)
    {
        if (File.Exists(path))
            EnsureReplaceableFile(path);
        var replacementSafetyPath = CreateSafetySnapshot(tempPath);
        var completed = false;
        try
        {
            var replacementFingerprint =
                CaptureFingerprint(replacementSafetyPath);
            if (!File.Exists(path))
            {
                CommitNewFile(
                    tempPath,
                    path,
                    replacementSafetyPath,
                    replacementFingerprint);
                completed = true;
                return;
            }

            CommitReplacement(
                tempPath,
                path,
                replacementFingerprint);
            completed = true;
        }
        finally
        {
            if (completed)
                DeleteIfExists(replacementSafetyPath);
        }
    }

    private void CommitNewFile(
        string tempPath,
        string path,
        string replacementSafetyPath,
        FileFingerprint replacementFingerprint)
    {
        try
        {
            MoveFileWithRetries(tempPath, path, overwrite: false);
        }
        catch
        {
            if (MatchesFingerprint(path, replacementFingerprint))
                return;
            if (!File.Exists(path))
                RestoreMissingDestination(replacementSafetyPath, path);
            throw;
        }

        using var committed = OpenPinnedRead(path);
        if (CaptureFingerprint(committed) != replacementFingerprint)
        {
            throw new IOException(
                "The atomically created file changed before it could be verified.");
        }
    }

    private void CommitReplacement(
        string tempPath,
        string path,
        FileFingerprint replacementFingerprint)
    {
        var backupPath = Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.backup");
        var originalSafetyPath = CreateSafetySnapshot(path);
        var originalFingerprint = CaptureFingerprint(originalSafetyPath);
        var committed = false;
        try
        {
            try
            {
                ReplaceFileWithRetries(tempPath, path, backupPath);
            }
            catch
            {
                committed =
                    MatchesFingerprint(path, replacementFingerprint) &&
                    MatchesFingerprint(backupPath, originalFingerprint);
                if (!committed)
                {
                    EnsureDestinationPresent(
                        path,
                        backupPath,
                        originalSafetyPath,
                        replacementFingerprint);
                    throw;
                }
            }

            using var committedStream = OpenPinnedRead(path);
            if (CaptureFingerprint(committedStream) != replacementFingerprint)
            {
                throw new IOException(
                    "The atomic replacement changed before it could be verified.");
            }
            if (!MatchesFingerprint(backupPath, originalFingerprint))
            {
                committedStream.Dispose();
                RestoreDisplacedDestination(
                    backupPath,
                    path,
                    replacementFingerprint);
                throw new IOException(
                    "The destination changed before the atomic replacement.");
            }

            committed = true;
            DeleteIfExists(backupPath);
        }
        finally
        {
            if (!committed &&
                !File.Exists(path))
            {
                EnsureDestinationPresent(
                    path,
                    backupPath,
                    originalSafetyPath,
                    replacementFingerprint);
            }
            DeleteIfExists(originalSafetyPath);
        }
    }

    private void EnsureDestinationPresent(
        string path,
        string backupPath,
        string originalSafetyPath,
        FileFingerprint replacementFingerprint)
    {
        if (File.Exists(path))
            return;
        var recoveryPath =
            File.Exists(backupPath) &&
            !MatchesFingerprint(backupPath, replacementFingerprint)
                ? backupPath
                : originalSafetyPath;
        if (File.Exists(recoveryPath))
            RestoreMissingDestination(recoveryPath, path);
    }

    private void RestoreDisplacedDestination(
        string backupPath,
        string path,
        FileFingerprint replacementFingerprint)
    {
        var displacedFingerprint = CaptureFingerprint(backupPath);
        var displacedSafetyPath = CreateSafetySnapshot(backupPath);
        var replacementPath = Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.displaced");
        var restored = false;
        try
        {
            try
            {
                ReplaceFileWithRetries(
                    backupPath,
                    path,
                    replacementPath);
                restored = true;
            }
            catch
            {
                restored =
                    MatchesFingerprint(path, displacedFingerprint) &&
                    MatchesFingerprint(
                        replacementPath,
                        replacementFingerprint);
                if (!restored)
                {
                    if (!File.Exists(path))
                    {
                        RestoreMissingDestination(
                            displacedSafetyPath,
                            path);
                    }
                    throw;
                }
            }

            if (!MatchesFingerprint(path, displacedFingerprint))
            {
                throw new IOException(
                    "The displaced destination could not be restored.");
            }
            if (MatchesFingerprint(
                    replacementPath,
                    replacementFingerprint))
            {
                DeleteIfExists(replacementPath);
            }
        }
        finally
        {
            if (restored)
                DeleteIfExists(displacedSafetyPath);
        }
    }

    private void RestoreMissingDestination(
        string recoveryPath,
        string path)
    {
        var restorePath = CreateSafetySnapshot(recoveryPath);
        try
        {
            File.Move(restorePath, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
        }
        finally
        {
            DeleteIfExists(restorePath);
        }
    }

    private void ReplaceFileWithRetries(
        string sourcePath,
        string destinationPath,
        string backupPath)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                _replaceFile(sourcePath, destinationPath, backupPath);
                return;
            }
            catch (Exception ex) when (
                attempt < 9 &&
                File.Exists(sourcePath) &&
                IsRetryableAtomicError(ex))
            {
                _delay(TimeSpan.FromMilliseconds(10 * (attempt + 1)));
            }
        }
    }

    private void MoveFileWithRetries(
        string sourcePath,
        string destinationPath,
        bool overwrite)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                _moveFile(sourcePath, destinationPath, overwrite);
                return;
            }
            catch (Exception ex) when (
                attempt < 9 &&
                File.Exists(sourcePath) &&
                IsRetryableAtomicError(ex))
            {
                _delay(TimeSpan.FromMilliseconds(10 * (attempt + 1)));
            }
        }
    }

    private static bool IsRetryableAtomicError(Exception exception)
    {
        if (exception is not IOException)
            return false;
        var errorCode = exception.HResult & 0xFFFF;
        return errorCode is 32 or 33 or 1175;
    }

    private static void EnsureReplaceableFile(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "Replacing a reparse-point file is not supported.");
        }
        if (!OperatingSystem.IsWindows())
            return;

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(
                stream.SafeFileHandle,
                out var information) ||
            information.NumberOfLinks != 1)
        {
            throw new IOException(
                "Replacing a hard-linked file is not supported.");
        }
        if (HasAlternateDataStreams(path))
        {
            throw new IOException(
                "Replacing a file with alternate data streams is not supported.");
        }
    }

    private static bool HasAlternateDataStreams(string path)
    {
        var findHandle = FindFirstStream(
            path,
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

    private static FileStream OpenPinnedRead(string path) =>
        new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

    private static FileFingerprint CaptureFingerprint(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return CaptureFingerprint(stream);
    }

    private static FileFingerprint CaptureFingerprint(Stream stream)
    {
        stream.Position = 0;
        return new FileFingerprint(
            stream.Length,
            Convert.ToHexString(SHA256.HashData(stream)));
    }

    private static bool MatchesFingerprint(
        string path,
        FileFingerprint expected)
    {
        try
        {
            return CaptureFingerprint(path) == expected;
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

    private static string CreateSafetySnapshot(string sourcePath)
    {
        var path = Path.Combine(
            Path.GetDirectoryName(sourcePath)!,
            $".{Path.GetFileName(sourcePath)}.{Guid.NewGuid():N}.snapshot");
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

    private static void DeleteIfExists(string path)
    {
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

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(
        string fileName,
        string existingFileName,
        IntPtr securityAttributes);

    private readonly record struct FileFingerprint(
        long Length,
        string ContentHash);

    public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);
    public void CopyFile(string source, string destination, bool overwrite = false) => File.Copy(source, destination, overwrite);
    public void MoveFile(string source, string destination, bool overwrite = false) => _moveFile(source, destination, overwrite);
    public void DeleteFile(string path) => File.Delete(path);
    public void DeleteDirectory(string path, bool recursive = false) => Directory.Delete(path, recursive);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public string GetTempPath() => Path.GetTempPath();
    public string CombinePath(params string[] paths) => Path.Combine(paths);
    public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);
    public string GetFileName(string path) => Path.GetFileName(path);
    public string GetFileNameWithoutExtension(string path) => Path.GetFileNameWithoutExtension(path);
    public string GetExtension(string path) => Path.GetExtension(path);
    public long GetFileSize(string path) => new FileInfo(path).Length;
    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
    public DateTime GetLastWriteTime(string path) => File.GetLastWriteTime(path);
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*", bool recursive = false)
        => Directory.EnumerateFiles(path, searchPattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
    public IEnumerable<string> EnumerateDirectories(string path) => Directory.EnumerateDirectories(path);
    public Stream OpenRead(string path) => File.OpenRead(path);
    public Stream OpenWrite(string path) => File.OpenWrite(path);
    public Stream OpenFile(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare share) =>
        new FileStream(path, mode, access, share);
    public IEnumerable<string> ReadLines(string path) => File.ReadLines(path);

    public IDisposable ProtectDirectoryTree(string path, string trustedRoot)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Protected autosave directory operations require Windows no-follow handles.");
        }

        var trusted = Path.GetFullPath(trustedRoot).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var directories = new Stack<string>();
        for (var directory = Path.GetFullPath(path);
             !string.Equals(directory, trusted, StringComparison.OrdinalIgnoreCase);
             directory = Path.GetDirectoryName(directory) ??
                 throw new InvalidDataException(
                     "The protected directory is outside its trusted root."))
        {
            directories.Push(directory);
        }

        var handles = new List<SafeFileHandle>();
        try
        {
            foreach (var directory in directories)
            {
                if (!Directory.Exists(directory))
                    continue;
                var handle = OpenNoFollow(
                    directory,
                    FileReadAttributes,
                    FileShareRead | FileShareWrite,
                    FileFlagBackupSemantics | FileFlagOpenReparsePoint);
                if (!GetFileInformationByHandle(handle, out var information) ||
                    (information.FileAttributes &
                     (uint)FileAttributes.ReparsePoint) != 0)
                {
                    handle.Dispose();
                    throw new InvalidDataException(
                        "The protected directory cannot be a reparse point.");
                }
                handles.Add(handle);
            }
            return new HandleCollection(handles);
        }
        catch
        {
            foreach (var handle in handles)
                handle.Dispose();
            throw;
        }
    }

    public string ReadAllTextNoFollow(string path)
    {
        var bytes = ReadAllBytesNoFollow(path);
        using var reader = new StreamReader(
            new MemoryStream(bytes),
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public byte[] ReadAllBytesNoFollow(string path)
    {
        using var stream = OpenReadNoFollow(path);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    public Stream OpenReadNoFollow(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Protected autosave reads require Windows no-follow handles.");
        }

        var handle = OpenNoFollow(
            path,
            GenericRead,
            FileShareRead | FileShareWrite | FileShareDelete,
            FileFlagOpenReparsePoint);
        try
        {
            EnsureRegularFile(handle);
            return new FileStream(handle, FileAccess.Read);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public void DeleteFileNoFollow(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Protected autosave deletion requires Windows no-follow handles.");
        }

        using var handle = OpenNoFollow(
            path,
            DeleteAccess | FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            FileFlagOpenReparsePoint);
        EnsureRegularFile(handle);
        var disposition = new FileDispositionInfo { DeleteFile = true };
        if (!SetFileInformationByHandle(
                handle,
                FileInfoByHandleClass.FileDispositionInfo,
                ref disposition,
                Marshal.SizeOf<FileDispositionInfo>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static SafeFileHandle OpenNoFollow(
        string path,
        uint access,
        uint share,
        uint flags)
    {
        var handle = CreateFile(
            path,
            access,
            share,
            IntPtr.Zero,
            OpenExisting,
            flags,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }
        return handle;
    }

    private static void EnsureRegularFile(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var information))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if ((information.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Refusing to follow a reparse-point file.");
    }

    private const uint GenericRead = 0x80000000;
    private const uint DeleteAccess = 0x00010000;
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out ByHandleFileInformation lpFileInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle hFile,
        FileInfoByHandleClass fileInformationClass,
        ref FileDispositionInfo lpFileInformation,
        int dwBufferSize);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        EntryPoint = "FindFirstStreamW",
        ExactSpelling = true,
        SetLastError = true)]
    private static extern IntPtr FindFirstStream(
        string fileName,
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

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool DeleteFile;
    }

    private enum FileInfoByHandleClass
    {
        FileDispositionInfo = 4
    }

    private sealed class HandleCollection(List<SafeFileHandle> handles) : IDisposable
    {
        public void Dispose()
        {
            foreach (var handle in handles)
                handle.Dispose();
        }
    }

}
