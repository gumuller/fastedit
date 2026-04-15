using System.Text;

namespace FastEdit.Services.Interfaces;

public interface IFileService
{
    Task<string> ReadAllTextAsync(string filePath);
    Task WriteAllTextAsync(string filePath, string content);
    Task<FileReadResult> ReadFileWithEncodingAsync(string filePath);
    Task WriteFileWithEncodingAsync(string filePath, string content, Encoding encoding, bool writeBom);
}

public record FileReadResult(string Content, Encoding Encoding, bool HasBom);
