using System.IO;
using System.Text;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;
using Moq;
using Xunit;

namespace FastEdit.Tests;

public class EditorTabViewModelTests
{
    private readonly Mock<IFileService> _fileService = new();
    private readonly Mock<IFileSystemService> _fileSystemService = new();
    private readonly Mock<IDialogService> _dialogService = new();

    private EditorTabViewModel CreateSut() =>
        new(_fileService.Object, _fileSystemService.Object, _dialogService.Object);

    // --- LoadFile ---

    [Fact]
    public async Task LoadFile_TextFile_SetsProperties()
    {
        var sut = CreateSut();
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "hello world");
            _fileService.Setup(f => f.ReadFileWithEncodingAsync(tempFile))
                .ReturnsAsync(new FileReadResult("hello world", Encoding.UTF8, false));

            await sut.LoadFileAsync(tempFile);

            Assert.Equal("hello world", sut.Content);
            Assert.Equal(Path.GetFileName(tempFile), sut.FileName);
            Assert.Equal(tempFile, sut.FilePath);
            Assert.False(sut.IsBinaryMode);
            Assert.False(sut.IsModified);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadFile_SetsEncoding()
    {
        var sut = CreateSut();
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test");
            _fileService.Setup(f => f.ReadFileWithEncodingAsync(tempFile))
                .ReturnsAsync(new FileReadResult("test", new UTF8Encoding(true), true));

            await sut.LoadFileAsync(tempFile);

            Assert.Contains("BOM", sut.Encoding);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- Save ---

    [Fact]
    public async Task Save_WithFilePath_WritesToFile()
    {
        var sut = CreateSut();
        sut.FilePath = @"C:\test.txt";
        sut.FileName = "test.txt";
        sut.Content = "content";

        await sut.SaveCommand.ExecuteAsync(null);

        _fileService.Verify(f => f.WriteFileWithEncodingAsync(
            @"C:\test.txt", "content", It.IsAny<Encoding>(), It.IsAny<bool>()), Times.Once);
        Assert.False(sut.IsModified);
    }

    [Fact]
    public async Task Save_Untitled_ShowsSaveDialog()
    {
        var sut = CreateSut();
        sut.FileName = "Untitled-1";
        sut.Content = "content";

        _dialogService.Setup(d => d.ShowSaveFileDialog(null, "Untitled-1", null))
            .Returns(@"C:\saved.txt");

        await sut.SaveCommand.ExecuteAsync(null);

        _dialogService.Verify(d => d.ShowSaveFileDialog(null, "Untitled-1", null), Times.Once);
        Assert.Equal(@"C:\saved.txt", sut.FilePath);
        Assert.Equal("saved.txt", sut.FileName);
    }

    [Fact]
    public async Task Save_Untitled_CancelDialog_DoesNotSave()
    {
        var sut = CreateSut();
        sut.FileName = "Untitled-1";
        sut.Content = "content";

        _dialogService.Setup(d => d.ShowSaveFileDialog(null, "Untitled-1", null))
            .Returns((string?)null);

        await sut.SaveCommand.ExecuteAsync(null);

        _fileService.Verify(f => f.WriteFileWithEncodingAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Encoding>(), It.IsAny<bool>()), Times.Never);
    }

    // --- SaveAs ---

    [Fact]
    public async Task SaveAs_TextFile_SavesWithNewName()
    {
        var sut = CreateSut();
        sut.FilePath = @"C:\old.txt";
        sut.FileName = "old.txt";
        sut.Content = "content";

        _dialogService.Setup(d => d.ShowSaveFileDialog(null, "old.txt", null))
            .Returns(@"C:\new.txt");

        await sut.SaveAsCommand.ExecuteAsync(null);

        _fileService.Verify(f => f.WriteFileWithEncodingAsync(
            @"C:\new.txt", "content", It.IsAny<Encoding>(), It.IsAny<bool>()), Times.Once);
        Assert.Equal(@"C:\new.txt", sut.FilePath);
        Assert.Equal("new.txt", sut.FileName);
        Assert.False(sut.IsModified);
    }

    [Fact]
    public async Task SaveAs_CancelDialog_DoesNotSave()
    {
        var sut = CreateSut();
        sut.FilePath = @"C:\old.txt";
        sut.FileName = "old.txt";

        _dialogService.Setup(d => d.ShowSaveFileDialog(null, "old.txt", null))
            .Returns((string?)null);

        await sut.SaveAsCommand.ExecuteAsync(null);

        _fileService.Verify(f => f.WriteFileWithEncodingAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Encoding>(), It.IsAny<bool>()), Times.Never);
    }

    // --- GetSyntaxLanguage ---

    [Theory]
    [InlineData("test.cs", "C#")]
    [InlineData("test.js", "JavaScript")]
    [InlineData("test.ts", "TypeScript")]
    [InlineData("test.py", "Python")]
    [InlineData("test.java", "Java")]
    [InlineData("test.cpp", "C++")]
    [InlineData("test.c", "C")]
    [InlineData("test.rs", "Rust")]
    [InlineData("test.go", "Go")]
    [InlineData("test.html", "HTML")]
    [InlineData("test.css", "CSS")]
    [InlineData("test.xml", "XML")]
    [InlineData("test.json", "JSON")]
    [InlineData("test.yaml", "YAML")]
    [InlineData("test.md", "Markdown")]
    [InlineData("test.sql", "SQL")]
    [InlineData("test.ps1", "PowerShell")]
    [InlineData("test.sh", "Shell")]
    [InlineData("test.toml", "TOML")]
    [InlineData("test.ini", "INI")]
    [InlineData("test.bat", "Batch")]
    [InlineData("test.unknown", "")]
    public async Task LoadFile_DetectsSyntaxLanguage(string fileName, string expectedLanguage)
    {
        var sut = CreateSut();
        var tempFile = Path.Combine(Path.GetTempPath(), fileName);
        try
        {
            File.WriteAllText(tempFile, "test content");
            _fileService.Setup(f => f.ReadFileWithEncodingAsync(tempFile))
                .ReturnsAsync(new FileReadResult("test content", Encoding.UTF8, false));

            await sut.LoadFileAsync(tempFile);

            Assert.Equal(expectedLanguage, sut.SyntaxLanguage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData("Dockerfile", "Dockerfile")]
    [InlineData("Makefile", "Makefile")]
    [InlineData(".gitignore", "INI")]
    [InlineData(".editorconfig", "INI")]
    public async Task LoadFile_DetectsLanguageByFilename(string fileName, string expectedLanguage)
    {
        var sut = CreateSut();
        var tempFile = Path.Combine(Path.GetTempPath(), fileName);
        try
        {
            File.WriteAllText(tempFile, "test content");
            _fileService.Setup(f => f.ReadFileWithEncodingAsync(tempFile))
                .ReturnsAsync(new FileReadResult("test content", Encoding.UTF8, false));

            await sut.LoadFileAsync(tempFile);

            Assert.Equal(expectedLanguage, sut.SyntaxLanguage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // --- Content change tracking ---

    [Fact]
    public void ContentChange_WithFilePath_SetsModified()
    {
        var sut = CreateSut();
        sut.FilePath = @"C:\test.txt";
        sut.IsModified = false;

        sut.Content = "changed";

        Assert.True(sut.IsModified);
    }

    [Fact]
    public void ContentChange_Untitled_DoesNotSetModified()
    {
        var sut = CreateSut();
        sut.FilePath = "";
        sut.IsModified = false;

        sut.Content = "changed";

        Assert.False(sut.IsModified);
    }

    // --- ToggleMode ---

    [Fact]
    public async Task ToggleMode_UntitledFile_DoesNothing()
    {
        var sut = CreateSut();
        sut.FilePath = "";
        sut.IsBinaryMode = false;

        await sut.ToggleModeCommand.ExecuteAsync(null);

        Assert.False(sut.IsBinaryMode);
    }

    [Fact]
    public async Task ToggleMode_ModifiedFile_DoesNothing()
    {
        var sut = CreateSut();
        sut.FilePath = @"C:\test.txt";
        sut.IsModified = true;
        sut.IsBinaryMode = false;

        await sut.ToggleModeCommand.ExecuteAsync(null);

        Assert.False(sut.IsBinaryMode);
    }

    // --- Dispose ---

    [Fact]
    public void Dispose_ClearsContent()
    {
        var sut = CreateSut();
        sut.Content = "hello";

        sut.Dispose();

        Assert.Equal(string.Empty, sut.Content);
    }

    [Fact]
    public void Dispose_CalledTwice_NoProblem()
    {
        var sut = CreateSut();
        sut.Dispose();
        sut.Dispose(); // Should not throw
    }
}
