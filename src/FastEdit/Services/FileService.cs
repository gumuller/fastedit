using System.IO;
using System.Text;
using FastEdit.Services.Interfaces;
using Microsoft.Win32;

namespace FastEdit.Services;

public class FileService : IFileService
{
    public Task<string?> ShowOpenFileDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "All Files (*.*)|*.*|Text Files (*.txt)|*.txt|Source Code|*.cs;*.js;*.ts;*.py;*.cpp;*.c;*.h;*.java;*.go;*.rs;*.rb",
            Title = "Open File"
        };

        var result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.FileName : null);
    }

    public Task<string?> ShowOpenFolderDialogAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Folder"
        };

        var result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.FolderName : null);
    }

    public Task<string?> ShowSaveFileDialogAsync(string? defaultFileName = null)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "All Files (*.*)|*.*",
            Title = "Save File",
            FileName = defaultFileName ?? string.Empty
        };

        var result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.FileName : null);
    }

    public async Task<string> ReadAllTextAsync(string filePath)
    {
        return await File.ReadAllTextAsync(filePath);
    }

    public async Task WriteAllTextAsync(string filePath, string content)
    {
        await File.WriteAllTextAsync(filePath, content);
    }

    public async Task<FileReadResult> ReadFileWithEncodingAsync(string filePath)
    {
        var bytes = await File.ReadAllBytesAsync(filePath);
        var (encoding, hasBom) = DetectEncoding(bytes);
        var content = encoding.GetString(bytes, hasBom ? encoding.Preamble.Length : 0, bytes.Length - (hasBom ? encoding.Preamble.Length : 0));
        return new FileReadResult(content, encoding, hasBom);
    }

    public async Task WriteFileWithEncodingAsync(string filePath, string content, Encoding encoding, bool writeBom)
    {
        var encodingToUse = writeBom ? encoding : GetEncodingWithoutBom(encoding);
        await File.WriteAllTextAsync(filePath, content, encodingToUse);
    }

    private static (Encoding encoding, bool hasBom) DetectEncoding(byte[] bytes)
    {
        if (bytes.Length == 0)
            return (new UTF8Encoding(false), false);

        // Check BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(true), true);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (Encoding.Unicode, true); // UTF-16 LE

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (Encoding.BigEndianUnicode, true); // UTF-16 BE

        // Check if valid UTF-8
        if (IsValidUtf8(bytes))
            return (new UTF8Encoding(false), false);

        // Fallback to system default (Windows-1252 on Windows)
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return (Encoding.GetEncoding(1252), false);
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        int i = 0;
        bool hasMultibyte = false;
        while (i < bytes.Length)
        {
            byte b = bytes[i];
            int continuationBytes;

            if (b <= 0x7F) { i++; continue; }
            else if (b >= 0xC2 && b <= 0xDF) { continuationBytes = 1; hasMultibyte = true; }
            else if (b >= 0xE0 && b <= 0xEF) { continuationBytes = 2; hasMultibyte = true; }
            else if (b >= 0xF0 && b <= 0xF4) { continuationBytes = 3; hasMultibyte = true; }
            else return false; // Invalid UTF-8 lead byte

            if (i + continuationBytes >= bytes.Length) return false;

            for (int j = 1; j <= continuationBytes; j++)
            {
                if ((bytes[i + j] & 0xC0) != 0x80) return false;
            }

            i += continuationBytes + 1;
        }

        // Pure ASCII is also valid UTF-8, treat as UTF-8
        return true;
    }

    private static Encoding GetEncodingWithoutBom(Encoding encoding)
    {
        if (encoding is UTF8Encoding)
            return new UTF8Encoding(false);
        // For other encodings, return as-is (UTF-16 always has BOM in .NET)
        return encoding;
    }
}
