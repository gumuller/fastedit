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
            Assert.Equal(FileOpenMode.Text, sut.Mode);
            Assert.False(sut.IsBinaryMode);
            Assert.False(sut.IsLargeFileMode);
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

        _dialogService.Setup(d => d.ShowSaveFileDialog(It.IsAny<string>(), "Untitled-1", null))
            .Returns(@"C:\saved.txt");

        await sut.SaveCommand.ExecuteAsync(null);

        _dialogService.Verify(d => d.ShowSaveFileDialog(It.IsAny<string>(), "Untitled-1", null), Times.Once);
        Assert.Equal(@"C:\saved.txt", sut.FilePath);
        Assert.Equal("saved.txt", sut.FileName);
    }

    [Fact]
    public async Task Save_Untitled_CancelDialog_DoesNotSave()
    {
        var sut = CreateSut();
        sut.FileName = "Untitled-1";
        sut.Content = "content";

        _dialogService.Setup(d => d.ShowSaveFileDialog(It.IsAny<string>(), "Untitled-1", null))
            .Returns((string?)null);

        await sut.SaveCommand.ExecuteAsync(null);

        _fileService.Verify(f => f.WriteFileWithEncodingAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Encoding>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Save_EditDuringWrite_RemainsModified()
    {
        var sut = CreateSut();
        sut.FilePath = @"C:\test.txt";
        sut.FileName = "test.txt";
        sut.Content = "snapshot";
        var writeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var writeCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _fileService.Setup(f => f.WriteFileWithEncodingAsync(
                sut.FilePath,
                "snapshot",
                It.IsAny<Encoding>(),
                It.IsAny<bool>()))
            .Returns(() =>
            {
                writeStarted.SetResult();
                return writeCompletion.Task;
            });

        var saveTask = sut.SaveCommand.ExecuteAsync(null);
        await writeStarted.Task;
        sut.Content = "newer edit";
        writeCompletion.SetResult();
        await saveTask;

        Assert.True(sut.IsModified);
        Assert.Equal("newer edit", sut.Content);
    }

    [Fact]
    public async Task Save_BinaryStateNotificationDoesNotIncrementUserMutationVersion()
    {
        var sut = CreateSut();
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(
                tempFile,
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            await sut.LoadFileAsync(tempFile);
            sut.ByteBuffer!.SetByte(7, 0x0B);
            var mutationVersion = sut.UserMutationVersion;

            await sut.SaveCommand.ExecuteAsync(null);

            Assert.Equal(mutationVersion, sut.UserMutationVersion);
            Assert.False(sut.IsModified);
        }
        finally
        {
            sut.Dispose();
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Save_Binary_Raises_ModificationsChanged_On_Calling_Thread()
    {
        var path = Path.GetTempFileName();
        EditorTabViewModel? sut = null;
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 0x00, 0x01, 0x02 });
            sut = CreateSut();
            await sut.LoadFileAsync(path);
            Assert.Equal(FileOpenMode.Binary, sut.Mode);
            var byteBuffer = Assert.IsType<FastEdit.Core.HexEngine.VirtualizedByteBuffer>(sut.ByteBuffer);
            byteBuffer.SetByte(0, 0xFF);

            var callingThread = Environment.CurrentManagedThreadId;
            var notificationThread = -1;
            byteBuffer.ModificationsChanged += (_, _) =>
                notificationThread = Environment.CurrentManagedThreadId;

            await sut.SaveCommand.ExecuteAsync(null);

            Assert.Equal(callingThread, notificationThread);
        }
        finally
        {
            sut?.Dispose();
            File.Delete(path);
        }
    }

    // --- SaveAs ---

    [Fact]
    public async Task SaveAs_TextFile_SavesWithNewName()
    {
        var sut = CreateSut();
        sut.FilePath = @"C:\old.txt";
        sut.FileName = "old.txt";
        sut.Content = "content";

        _dialogService.Setup(d => d.ShowSaveFileDialog(It.IsAny<string>(), "old.txt", null))
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

        _dialogService.Setup(d => d.ShowSaveFileDialog(It.IsAny<string>(), "old.txt", null))
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
    public void ContentChange_Untitled_SetsModified()
    {
        var sut = CreateSut();
        sut.FilePath = "";
        sut.IsModified = false;

        sut.Content = "changed";

        Assert.True(sut.IsModified);
    }

    [Fact]
    public void SetContentBaseline_CanEstablishCleanLoadedState()
    {
        var sut = CreateSut();

        sut.SetContentBaseline("loaded", isModified: false);

        Assert.Equal("loaded", sut.Content);
        Assert.False(sut.IsModified);
    }

    [Fact]
    public void ReplaceContentFromDisk_Establishes_Clean_External_Baseline()
    {
        var sut = CreateSut();
        sut.FilePath = @"C:\test.txt";
        sut.Content = "local edit";
        Assert.True(sut.IsModified);

        sut.ReplaceContentFromDisk("external content");

        Assert.Equal("external content", sut.Content);
        Assert.False(sut.IsModified);
    }

    // --- ToggleMode ---

    [Theory]
    [InlineData(FileOpenMode.Text, false, false)]
    [InlineData(FileOpenMode.Binary, true, false)]
    [InlineData(FileOpenMode.LargeText, false, true)]
    public void Mode_UpdatesCompatibilityProperties(
        FileOpenMode mode,
        bool expectedBinaryMode,
        bool expectedLargeFileMode)
    {
        var sut = CreateSut();

        sut.Mode = mode;

        Assert.Equal(expectedBinaryMode, sut.IsBinaryMode);
        Assert.Equal(expectedLargeFileMode, sut.IsLargeFileMode);
    }

    [Fact]
    public async Task ToggleMode_UntitledFile_DoesNothing()
    {
        var sut = CreateSut();
        sut.FilePath = "";
        sut.Mode = FileOpenMode.Text;

        await sut.ToggleModeCommand.ExecuteAsync(null);

        Assert.Equal(FileOpenMode.Text, sut.Mode);
        Assert.False(sut.IsBinaryMode);
    }

    [Fact]
    public async Task ToggleMode_ModifiedFile_DoesNothing()
    {
        var sut = CreateSut();
        sut.FilePath = @"C:\test.txt";
        sut.IsModified = true;
        sut.Mode = FileOpenMode.Text;

        await sut.ToggleModeCommand.ExecuteAsync(null);

        Assert.Equal(FileOpenMode.Text, sut.Mode);
        Assert.False(sut.IsBinaryMode);
    }

    [Fact]
    public async Task ToggleMode_BinaryToText_EstablishesCleanBaseline()
    {
        var sut = CreateSut();
        sut.FilePath = @"C:\test.txt";
        sut.FileSize = 10;
        sut.Mode = FileOpenMode.Binary;
        sut.IsModified = false;
        _fileService.Setup(f => f.ReadFileWithEncodingAsync(sut.FilePath))
            .ReturnsAsync(new FileReadResult("text", Encoding.UTF8, false));

        await sut.ToggleModeCommand.ExecuteAsync(null);

        Assert.Equal(FileOpenMode.Text, sut.Mode);
        Assert.Equal("text", sut.Content);
        Assert.False(sut.IsModified);
    }

    [Fact]
    public async Task Save_BinaryFile_DoesNotIncrementUserChangeVersionWhenClearingModifications()
    {
        var sut = CreateSut();
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0, 1, 0, 2, 0, 3 });
            await sut.LoadFileAsync(tempFile);
            Assert.Equal(FileOpenMode.Binary, sut.Mode);
            sut.ByteBuffer!.SetByte(0, 9);
            var userChangeVersion = sut.ChangeVersion;

            await sut.SaveCommand.ExecuteAsync(null);

            Assert.False(sut.IsModified);
            Assert.Equal(userChangeVersion, sut.ChangeVersion);
        }
        finally
        {
            sut.Dispose();
            File.Delete(tempFile);
        }
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
