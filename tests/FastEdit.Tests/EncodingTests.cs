using System.IO;
using System.Text;
using FastEdit.Services;
using FastEdit.Services.Interfaces;

namespace FastEdit.Tests;

public class EncodingTests
{
    private readonly FileService _fileService = new();

    [Fact]
    public async Task ReadFile_Detects_UTF8_Without_BOM()
    {
        var path = CreateTempFile("Hello UTF-8", new UTF8Encoding(false));
        try
        {
            var result = await _fileService.ReadFileWithEncodingAsync(path);
            Assert.Equal("Hello UTF-8", result.Content);
            Assert.False(result.HasBom);
            Assert.IsType<UTF8Encoding>(result.Encoding);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReadFile_Detects_UTF8_With_BOM()
    {
        var path = CreateTempFile("Hello BOM", new UTF8Encoding(true));
        try
        {
            var result = await _fileService.ReadFileWithEncodingAsync(path);
            Assert.Equal("Hello BOM", result.Content);
            Assert.True(result.HasBom);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReadFile_Detects_UTF16LE()
    {
        var path = CreateTempFile("Hello UTF-16", Encoding.Unicode);
        try
        {
            var result = await _fileService.ReadFileWithEncodingAsync(path);
            Assert.Equal("Hello UTF-16", result.Content);
            Assert.True(result.HasBom);
            Assert.Equal("utf-16", result.Encoding.WebName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReadFile_Detects_UTF16BE()
    {
        var path = CreateTempFile("Hello BE", Encoding.BigEndianUnicode);
        try
        {
            var result = await _fileService.ReadFileWithEncodingAsync(path);
            Assert.Equal("Hello BE", result.Content);
            Assert.True(result.HasBom);
            Assert.Equal("utf-16BE", result.Encoding.WebName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReadFile_Empty_File_Returns_Empty_Content()
    {
        var path = Path.GetTempFileName();
        try
        {
            var result = await _fileService.ReadFileWithEncodingAsync(path);
            Assert.Equal("", result.Content);
            Assert.False(result.HasBom);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task WriteFile_Preserves_Encoding_Round_Trip()
    {
        var originalContent = "Round-trip test with special chars: é à ü ñ";
        var path = Path.GetTempFileName();
        try
        {
            var encoding = new UTF8Encoding(true);
            await _fileService.WriteFileWithEncodingAsync(path, originalContent, encoding, true);

            var result = await _fileService.ReadFileWithEncodingAsync(path);
            Assert.Equal(originalContent, result.Content);
            Assert.True(result.HasBom);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task WriteFile_Without_BOM()
    {
        var path = Path.GetTempFileName();
        try
        {
            await _fileService.WriteFileWithEncodingAsync(path, "No BOM", new UTF8Encoding(false), false);

            var bytes = await File.ReadAllBytesAsync(path);
            // UTF-8 BOM is EF BB BF - should not be present
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReadFile_ASCII_Content_Detected_As_UTF8()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "Pure ASCII content 123", new UTF8Encoding(false));
        try
        {
            var result = await _fileService.ReadFileWithEncodingAsync(path);
            Assert.Equal("Pure ASCII content 123", result.Content);
            Assert.False(result.HasBom);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReadFile_ContentBeyondDetectionSample_IsReadCompletely()
    {
        var path = Path.GetTempFileName();
        var content = new string('a', 4096) + "tail text beyond sample";
        try
        {
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));

            var result = await _fileService.ReadFileWithEncodingAsync(path);

            Assert.Equal(content, result.Content);
            Assert.False(result.HasBom);
            Assert.Equal("utf-8", result.Encoding.WebName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReadFile_Utf8SequenceCrossingDetectionSampleBoundary_RemainsUtf8()
    {
        var path = Path.GetTempFileName();
        var content = new string('a', 4095) + "é after boundary";
        try
        {
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));

            var result = await _fileService.ReadFileWithEncodingAsync(path);

            Assert.Equal(content, result.Content);
            Assert.Equal("utf-8", result.Encoding.WebName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReadFile_InvalidUtf8Sample_FallsBackToWindows1252()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [0x48, 0xE9, 0x6C, 0x6C, 0x6F]);

            var result = await _fileService.ReadFileWithEncodingAsync(path);

            Assert.Equal("Héllo", result.Content);
            Assert.False(result.HasBom);
            Assert.Equal("windows-1252", result.Encoding.WebName);
        }
        finally { File.Delete(path); }
    }

    private static string CreateTempFile(string content, Encoding encoding)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content, encoding);
        return path;
    }
}
