using System.IO;
using FastEdit.Core.FileAnalysis;

namespace FastEdit.Tests;

public class BinaryDetectorTests
{
    private readonly BinaryDetector _detector = new();

    [Fact]
    public async Task IsBinaryFileAsync_Returns_False_For_Text_File()
    {
        var path = CreateTempFile("Hello, this is a plain text file.\nLine 2\nLine 3");
        try
        {
            Assert.False(await _detector.IsBinaryFileAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task IsBinaryFileAsync_Returns_True_For_Binary_File()
    {
        var bytes = new byte[256];
        for (int i = 0; i < 256; i++) bytes[i] = (byte)i;
        var path = CreateTempBinaryFile(bytes);
        try
        {
            Assert.True(await _detector.IsBinaryFileAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task IsBinaryFileAsync_Returns_True_For_Null_Bytes()
    {
        var bytes = new byte[] { 0x48, 0x65, 0x6C, 0x00, 0x6C, 0x6F }; // "Hel\0lo"
        var path = CreateTempBinaryFile(bytes);
        try
        {
            Assert.True(await _detector.IsBinaryFileAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task AnalyzeFileAsync_Returns_Analysis_Result()
    {
        var path = CreateTempFile("Simple text content");
        try
        {
            var result = await _detector.AnalyzeFileAsync(path);

            Assert.False(result.IsBinary);
            Assert.NotNull(result.DetectedEncoding);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task AnalyzeFileAsync_Detects_UTF8()
    {
        var path = CreateTempFile("Hello World — UTF-8 text with special chars: é à ü");
        try
        {
            var result = await _detector.AnalyzeFileAsync(path);

            Assert.False(result.IsBinary);
            Assert.Contains("UTF", result.DetectedEncoding ?? "", StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task IsBinaryFileAsync_Handles_Empty_File()
    {
        var path = CreateTempFile("");
        try
        {
            // Empty file should not be binary
            Assert.False(await _detector.IsBinaryFileAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task IsBinaryFileAsync_Handles_Small_Text_File()
    {
        var path = CreateTempFile("Hi");
        try
        {
            Assert.False(await _detector.IsBinaryFileAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task AnalyzeFileAsync_Binary_File_Has_No_Text_Encoding()
    {
        var bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var path = CreateTempBinaryFile(bytes);
        try
        {
            var result = await _detector.AnalyzeFileAsync(path);
            Assert.True(result.IsBinary);
        }
        finally { File.Delete(path); }
    }

    private static string CreateTempFile(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path;
    }

    private static string CreateTempBinaryFile(byte[] content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, content);
        return path;
    }
}
