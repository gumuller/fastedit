using System.IO;
using System.Text;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public class FileSystemService : IFileSystemService
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
    public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);
    public void CopyFile(string source, string destination, bool overwrite = false) => File.Copy(source, destination, overwrite);
    public void DeleteFile(string path) => File.Delete(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public string GetTempPath() => Path.GetTempPath();
    public string CombinePath(params string[] paths) => Path.Combine(paths);
    public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);
    public string GetFileName(string path) => Path.GetFileName(path);
    public string GetFileNameWithoutExtension(string path) => Path.GetFileNameWithoutExtension(path);
    public string GetExtension(string path) => Path.GetExtension(path);
    public long GetFileSize(string path) => new FileInfo(path).Length;
    public DateTime GetLastWriteTime(string path) => File.GetLastWriteTime(path);
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*", bool recursive = false)
        => Directory.EnumerateFiles(path, searchPattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
    public IEnumerable<string> EnumerateDirectories(string path) => Directory.EnumerateDirectories(path);
    public Stream OpenRead(string path) => File.OpenRead(path);
    public Stream OpenWrite(string path) => File.OpenWrite(path);
    public IEnumerable<string> ReadLines(string path) => File.ReadLines(path);
}
