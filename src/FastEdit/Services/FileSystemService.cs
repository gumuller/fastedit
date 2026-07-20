using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using FastEdit.Services.Interfaces;
using Microsoft.Win32.SafeHandles;

namespace FastEdit.Services;

public class FileSystemService : IFileSystemService, ISecureFileSystemService
{
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
    {
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
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
                File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, path);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
    public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);
    public void CopyFile(string source, string destination, bool overwrite = false) => File.Copy(source, destination, overwrite);
    public void MoveFile(string source, string destination, bool overwrite = false) => File.Move(source, destination, overwrite);
    public void DeleteFile(string path) => File.Delete(path);
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
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Protected autosave reads require Windows no-follow handles.");
        }

        using var handle = OpenNoFollow(
            path,
            GenericRead,
            FileShareRead | FileShareWrite | FileShareDelete,
            FileFlagOpenReparsePoint);
        EnsureRegularFile(handle);
        using var stream = new FileStream(handle, FileAccess.Read);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
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
