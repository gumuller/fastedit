using System.IO;
using System.Text;

namespace FastEdit.Services.Interfaces;

public interface IFileSystemService
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string[] GetFiles(string path, string searchPattern = "*", bool recursive = false);
    string[] GetDirectories(string path);
    string ReadAllText(string path);
    string ReadAllText(string path, Encoding encoding);
    byte[] ReadAllBytes(string path);
    Task<byte[]> ReadAllBytesAsync(string path);
    Task<string> ReadAllTextAsync(string path);
    void WriteAllText(string path, string content);
    void WriteAllText(string path, string content, Encoding encoding);
    void WriteAllBytes(string path, byte[] bytes);
    void CopyFile(string source, string destination, bool overwrite = false);
    void DeleteFile(string path);
    void CreateDirectory(string path);
    string GetTempPath();
    string CombinePath(params string[] paths);
    string? GetDirectoryName(string path);
    string GetFileName(string path);
    string GetFileNameWithoutExtension(string path);
    string GetExtension(string path);
    long GetFileSize(string path);
    DateTime GetLastWriteTime(string path);
    IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*", bool recursive = false);
    IEnumerable<string> EnumerateDirectories(string path);
    Stream OpenRead(string path);
    Stream OpenWrite(string path);
    IEnumerable<string> ReadLines(string path);
}
