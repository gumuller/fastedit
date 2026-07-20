using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FastEdit.Helpers;
using FastEdit.Infrastructure;
using FastEdit.Services.Interfaces;
using FastEdit.Theming;
using FastEdit.ViewModels;
using Moq;
using Xunit;

namespace FastEdit.Tests;

public class MainViewModelTests
{
    private readonly Mock<IFileService> _fileService = new();
    private readonly Mock<IThemeService> _themeService = new();
    private readonly Mock<ISettingsService> _settingsService = new();
    private readonly Mock<IDialogService> _dialogService = new();
    private readonly Mock<IFileSystemService> _fileSystemService = new();
    private readonly Mock<IEditorTabFactory> _tabFactory = new();
    private readonly Mock<IWorkspaceService> _workspaceService = new();
    private readonly FileTreeViewModel _fileTree;
    private readonly MainViewModel _sut;

    public MainViewModelTests()
    {
        _themeService.Setup(t => t.AvailableThemes).Returns(new List<ThemeDefinition>());
        _themeService.Setup(t => t.CurrentTheme).Returns((ThemeDefinition)null!);
        _settingsService.Setup(s => s.RecentFiles).Returns(new List<string>());
        _settingsService.Setup(s => s.WordWrapEnabled).Returns(false);
        _settingsService.Setup(s => s.ShowWhitespace).Returns(false);
        _settingsService.Setup(s => s.EditorFontSize).Returns(14);
        _fileSystemService.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(true);
        _fileSystemService.Setup(f => f.GetDirectories(It.IsAny<string>())).Returns(Array.Empty<string>());
        _fileSystemService.Setup(f => f.GetFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>())).Returns(Array.Empty<string>());

        _fileTree = new FileTreeViewModel(
            _fileService.Object,
            _settingsService.Object,
            new Mock<IDialogService>().Object,
            _fileSystemService.Object);

        _sut = new MainViewModel(
            _fileService.Object,
            _themeService.Object,
            _settingsService.Object,
            _dialogService.Object,
            _fileSystemService.Object,
            _tabFactory.Object,
            _workspaceService.Object,
            _fileTree);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private EditorTabViewModel CreateMockTab(
        string fileName = "test.txt",
        string filePath = "",
        bool isModified = false,
        FileOpenMode mode = FileOpenMode.Text)
    {
        var tab = new EditorTabViewModel(_fileService.Object, _fileSystemService.Object, _dialogService.Object)
        {
            FileName = fileName,
            FilePath = filePath,
            IsModified = isModified,
            Mode = mode
        };
        return tab;
    }

    [Fact]
    public void TextToUpperCaseCommand_RaisesTextToolOperation()
    {
        TextToolOperation? operation = null;
        _sut.TextToolRequested += requestedOperation => operation = requestedOperation;

        _sut.TextToUpperCaseCommand.Execute(null);

        Assert.Equal(TextToolOperation.UpperCase, operation);
    }

    [Fact]
    public void TextToUpperCaseCommand_LargeTextMode_DoesNotRaiseTextToolOperation()
    {
        _sut.SelectedTab = CreateMockTab(mode: FileOpenMode.LargeText);
        var wasRaised = false;
        _sut.TextToolRequested += _ => wasRaised = true;

        _sut.TextToUpperCaseCommand.Execute(null);

        Assert.False(wasRaised);
        Assert.Contains("Large file viewer", _sut.StatusText);
        Assert.Contains("read-only", _sut.StatusText);
    }

    [Fact]
    public async Task SaveCommand_LargeTextMode_ReportsReadOnlyStatus()
    {
        _sut.SelectedTab = CreateMockTab(mode: FileOpenMode.LargeText);

        await _sut.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Large file viewer is read-only; original file unchanged.", _sut.StatusText);
    }

    [Fact]
    public void TextChecksumMd5Command_RaisesChecksumOperation()
    {
        TextToolOperation? operation = null;
        _sut.TextToolRequested += requestedOperation => operation = requestedOperation;

        _sut.TextChecksumMd5Command.Execute(null);

        Assert.Equal(TextToolOperation.ComputeMd5, operation);
    }

    [Fact]
    public void ShowCompletionCommand_SmallTextFile_RaisesCompletionEvent()
    {
        var tab = CreateMockTab();
        tab.FileSize = 1024;
        _sut.SelectedTab = tab;
        var wasRaised = false;
        _sut.ShowCompletionRequested += () => wasRaised = true;

        _sut.ShowCompletionCommand.Execute(null);

        Assert.True(wasRaised);
    }

    [Fact]
    public void ShowCompletionCommand_LargeTextFile_IsBlockedByFeatureGate()
    {
        var tab = CreateMockTab();
        tab.FileSize = EditorFeatureGatePolicy.DisableAdvancedFeaturesThresholdBytes;
        _sut.SelectedTab = tab;
        var wasRaised = false;
        _sut.ShowCompletionRequested += () => wasRaised = true;

        _sut.ShowCompletionCommand.Execute(null);

        Assert.False(wasRaised);
        Assert.Contains("disabled for performance", _sut.StatusText);
    }

    [Fact]
    public void ToggleFilterPanelCommand_RaisesToggleEventOnce()
    {
        var raisedCount = 0;
        _sut.ToggleFilterPanelRequested += () => raisedCount++;

        _sut.ToggleFilterPanelCommand.Execute(null);

        Assert.True(_sut.IsFilterPanelVisible);
        Assert.Equal(1, raisedCount);
    }

    [Fact]
    public void IsFilterPanelVisibleChanged_RaisesToggleEvent()
    {
        var wasRaised = false;
        _sut.ToggleFilterPanelRequested += () => wasRaised = true;

        _sut.IsFilterPanelVisible = true;

        Assert.True(wasRaised);
    }

    [Fact]
    public void SelectedTab_LargeTextMode_Shows_LargeFileViewer_Status()
    {
        var tab = CreateMockTab(mode: FileOpenMode.LargeText);
        tab.FileSize = 123L * 1024 * 1024;
        tab.Encoding = "UTF-8";

        _sut.SelectedTab = tab;

        Assert.Equal("Large file viewer: indexing lines, read-only | 123 MB | UTF-8", _sut.StatusText);
    }

    // --- NewFile ---

    [Fact]
    public void NewFile_AddsUntitledTab()
    {
        var tab = CreateMockTab("Untitled");
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);

        _sut.NewFileCommand.Execute(null);

        Assert.Single(_sut.Tabs);
        Assert.Equal(_sut.Tabs[0], _sut.SelectedTab);
    }

    [Fact]
    public void NewFile_IncrementsTabNumber()
    {
        var tab1 = CreateMockTab("Untitled");
        var tab2 = CreateMockTab("Untitled");
        _tabFactory.SetupSequence(f => f.CreateUntitled(null)).Returns(tab1).Returns(tab2);

        _sut.NewFileCommand.Execute(null);
        _sut.NewFileCommand.Execute(null);

        Assert.Equal(2, _sut.Tabs.Count);
        Assert.Equal("Untitled-2", _sut.Tabs[1].FileName);
    }

    // --- OpenFile ---

    [Fact]
    public async Task OpenFile_WithPath_AddsTab()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "hello");
            var tab = CreateMockTab("hello.txt", tempFile);
            _tabFactory.Setup(f => f.Create()).Returns(tab);
            _fileService.Setup(f => f.ReadFileWithEncodingAsync(tempFile))
                .ReturnsAsync(new FileReadResult("hello", System.Text.Encoding.UTF8, false));

            await _sut.OpenFileCommand.ExecuteAsync(tempFile);

            Assert.Single(_sut.Tabs);
            Assert.Equal(tab, _sut.SelectedTab);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task OpenFile_NoPath_ShowsDialog()
    {
        _dialogService.Setup(d => d.ShowOpenFileDialog(It.IsAny<string>(), null)).Returns((string?)null);

        await _sut.OpenFileCommand.ExecuteAsync(null);

        _dialogService.Verify(d => d.ShowOpenFileDialog(It.IsAny<string>(), null), Times.Once);
        Assert.Empty(_sut.Tabs);
    }

    [Fact]
    public async Task OpenFile_AlreadyOpen_SelectsExistingTab()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "hello");
            var tab = CreateMockTab("hello.txt", tempFile);
            _tabFactory.Setup(f => f.Create()).Returns(tab);
            _fileService.Setup(f => f.ReadFileWithEncodingAsync(tempFile))
                .ReturnsAsync(new FileReadResult("hello", System.Text.Encoding.UTF8, false));

            await _sut.OpenFileCommand.ExecuteAsync(tempFile);
            await _sut.OpenFileCommand.ExecuteAsync(tempFile);

            Assert.Single(_sut.Tabs);
            _tabFactory.Verify(f => f.Create(), Times.Once);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- CloseTab ---

    [Fact]
    public async Task CloseTab_UnmodifiedTab_RemovesImmediately()
    {
        var tab = CreateMockTab("test.txt");
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);

        await _sut.CloseTabCoreAsync(tab);

        Assert.Empty(_sut.Tabs);
        Assert.Null(_sut.SelectedTab);
    }

    [Fact]
    public async Task CloseTab_ModifiedTab_Cancel_KeepsTab()
    {
        var tab = CreateMockTab("test.txt", isModified: true);
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);
        tab.IsModified = true;

        _dialogService.Setup(d => d.ShowMessage(
            It.IsAny<string>(), It.IsAny<string>(),
            DialogButtons.YesNoCancel, DialogIcon.Warning))
            .Returns(Services.Interfaces.DialogResult.Cancel);

        await _sut.CloseTabCoreAsync(tab);

        Assert.Single(_sut.Tabs);
    }

    [Fact]
    public async Task CloseTab_EditedUntitledTab_PromptsAndKeepsTabOnCancel()
    {
        var tab = CreateMockTab("Untitled-1");
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);
        tab.Content = "unsaved";
        _dialogService.Setup(d => d.ShowMessage(
                It.IsAny<string>(), It.IsAny<string>(),
                DialogButtons.YesNoCancel, DialogIcon.Warning))
            .Returns(Services.Interfaces.DialogResult.Cancel);

        await _sut.CloseTabCoreAsync(tab);

        Assert.True(tab.IsModified);
        Assert.Single(_sut.Tabs);
        _dialogService.Verify(d => d.ShowMessage(
            It.Is<string>(message => message.Contains("Untitled-1")),
            It.IsAny<string>(),
            DialogButtons.YesNoCancel,
            DialogIcon.Warning), Times.Once);
    }

    [Fact]
    public async Task CloseTab_ModifiedTab_No_RemovesWithoutSaving()
    {
        var tab = CreateMockTab("test.txt", isModified: true);
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);
        tab.IsModified = true;

        _dialogService.Setup(d => d.ShowMessage(
            It.IsAny<string>(), It.IsAny<string>(),
            DialogButtons.YesNoCancel, DialogIcon.Warning))
            .Returns(Services.Interfaces.DialogResult.No);

        await _sut.CloseTabCoreAsync(tab);

        Assert.Empty(_sut.Tabs);
    }

    [Fact]
    public async Task CloseTab_SelectsAdjacentTab()
    {
        var tab1 = CreateMockTab("a.txt");
        var tab2 = CreateMockTab("b.txt");
        _tabFactory.SetupSequence(f => f.CreateUntitled(null)).Returns(tab1).Returns(tab2);

        _sut.NewFileCommand.Execute(null);
        _sut.NewFileCommand.Execute(null);
        _sut.SelectedTab = tab1;

        await _sut.CloseTabCoreAsync(tab1);

        Assert.Single(_sut.Tabs);
        Assert.Equal(tab2, _sut.SelectedTab);
    }

    // --- ToggleWordWrap ---

    [Fact]
    public void ToggleWordWrap_TogglesProperty()
    {
        Assert.False(_sut.IsWordWrapEnabled);

        _sut.ToggleWordWrapCommand.Execute(null);

        Assert.True(_sut.IsWordWrapEnabled);
        _settingsService.VerifySet(s => s.WordWrapEnabled = true);
    }

    [Fact]
    public void ToggleWordWrap_UpdatesStatusText()
    {
        _sut.ToggleWordWrapCommand.Execute(null);
        Assert.Contains("On", _sut.StatusText);

        _sut.ToggleWordWrapCommand.Execute(null);
        Assert.Contains("Off", _sut.StatusText);
    }

    // --- ToggleWhitespace ---

    [Fact]
    public void ToggleWhitespace_TogglesProperty()
    {
        _sut.ToggleWhitespaceCommand.Execute(null);
        Assert.True(_sut.IsWhitespaceVisible);
        _settingsService.VerifySet(s => s.ShowWhitespace = true);
    }

    // --- Zoom ---

    [Fact]
    public void ZoomIn_IncreasesFontSize()
    {
        var initial = _sut.EditorFontSize;
        _sut.ZoomInCommand.Execute(null);
        Assert.Equal(initial + 2, _sut.EditorFontSize);
    }

    [Fact]
    public void ZoomOut_DecreasesFontSize()
    {
        var initial = _sut.EditorFontSize;
        _sut.ZoomOutCommand.Execute(null);
        Assert.Equal(initial - 2, _sut.EditorFontSize);
    }

    [Fact]
    public void ZoomIn_CapsAt72()
    {
        _sut.EditorFontSize = 72;
        _sut.ZoomInCommand.Execute(null);
        Assert.Equal(72, _sut.EditorFontSize);
    }

    [Fact]
    public void ZoomOut_CapsAt8()
    {
        _sut.EditorFontSize = 8;
        _sut.ZoomOutCommand.Execute(null);
        Assert.Equal(8, _sut.EditorFontSize);
    }

    [Fact]
    public void ResetZoom_SetsTo14()
    {
        _sut.EditorFontSize = 30;
        _sut.ResetZoomCommand.Execute(null);
        Assert.Equal(14, _sut.EditorFontSize);
    }

    // --- ChangeTheme ---

    [Fact]
    public void ChangeTheme_AppliesAndPersists()
    {
        _sut.ChangeThemeCommand.Execute("Monokai");

        _themeService.Verify(t => t.ApplyTheme("Monokai"), Times.Once);
        _settingsService.VerifySet(s => s.ThemeName = "Monokai");
        Assert.Equal("Monokai", _sut.CurrentThemeName);
    }

    // --- ConvertLineEndings ---

    [Fact]
    public void ConvertLineEndings_ConvertsContent()
    {
        var tab = CreateMockTab("test.txt");
        tab.Content = "line1\r\nline2\r\n";
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);

        _sut.ConvertLineEndingsCommand.Execute("LF");

        Assert.Equal("line1\nline2\n", _sut.SelectedTab!.Content);
        Assert.Equal("LF", _sut.LineEnding);
    }

    [Fact]
    public void ConvertLineEndings_NoTab_DoesNothing()
    {
        _sut.ConvertLineEndingsCommand.Execute("LF");
        // Should not throw
    }

    // --- OpenRecentFile ---

    [Fact]
    public async Task OpenRecentFile_FileNotFound_RemovesFromRecent()
    {
        _fileSystemService.Setup(f => f.FileExists(@"C:\gone.txt")).Returns(false);
        _sut.RecentFiles.Add(@"C:\gone.txt");

        await _sut.OpenRecentFileCommand.ExecuteAsync(@"C:\gone.txt");

        Assert.DoesNotContain(@"C:\gone.txt", _sut.RecentFiles);
    }

    // --- HasUnsavedChanges ---

    [Fact]
    public void HasUnsavedChanges_NoTabs_ReturnsFalse()
    {
        Assert.False(_sut.HasUnsavedChanges());
    }

    [Fact]
    public void HasUnsavedChanges_WithModifiedTab_ReturnsTrue()
    {
        var tab = CreateMockTab("test.txt", isModified: true);
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);
        tab.IsModified = true;

        Assert.True(_sut.HasUnsavedChanges());
    }

    // --- ConfirmExit ---

    [Fact]
    public async Task ConfirmExit_NoUnsaved_ReturnsTrue()
    {
        Assert.True(await _sut.ConfirmExitAsync());
    }

    [Fact]
    public async Task ConfirmExit_Cancelled_ReturnsFalse()
    {
        var tab = CreateMockTab("test.txt", isModified: true);
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);
        tab.IsModified = true;

        _dialogService.Setup(d => d.ShowMessage(
            It.IsAny<string>(), It.IsAny<string>(),
            DialogButtons.YesNoCancel, DialogIcon.Warning))
            .Returns(Services.Interfaces.DialogResult.Cancel);

        Assert.False(await _sut.ConfirmExitAsync());
    }

    [Fact]
    public async Task ConfirmExit_EditedUntitledTab_PromptsAndCanCancel()
    {
        var tab = CreateMockTab("Untitled-1");
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);
        tab.Content = "unsaved";
        _dialogService.Setup(d => d.ShowMessage(
                It.IsAny<string>(), It.IsAny<string>(),
                DialogButtons.YesNoCancel, DialogIcon.Warning))
            .Returns(Services.Interfaces.DialogResult.Cancel);

        var canExit = await _sut.ConfirmExitAsync();

        Assert.False(canExit);
        Assert.True(tab.IsModified);
    }

    [Fact]
    public async Task ConfirmExit_No_ReturnsTrue()
    {
        var tab = CreateMockTab("test.txt", isModified: true);
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);
        tab.IsModified = true;

        _dialogService.Setup(d => d.ShowMessage(
            It.IsAny<string>(), It.IsAny<string>(),
            DialogButtons.YesNoCancel, DialogIcon.Warning))
            .Returns(Services.Interfaces.DialogResult.No);

        Assert.True(await _sut.ConfirmExitAsync());
    }

    [Fact]
    public async Task PrepareForExit_NoThenShutdownSession_ExcludesOnlyDiscardedUntitledContent()
    {
        var discarded = CreateMockTab("Untitled-discarded");
        var retained = CreateMockTab("Untitled-retained");
        _tabFactory.SetupSequence(f => f.CreateUntitled(null))
            .Returns(discarded)
            .Returns(retained);
        _sut.NewFileCommand.Execute(null);
        _sut.NewFileCommand.Execute(null);
        discarded.Content = "discard me";
        retained.SetContentBaseline("restore me", isModified: false);
        _dialogService.Setup(d => d.ShowMessage(
                It.IsAny<string>(), It.IsAny<string>(),
                DialogButtons.YesNoCancel, DialogIcon.Warning))
            .Returns(Services.Interfaces.DialogResult.No);

        Assert.True(await _sut.PrepareForExitAsync());
        _sut.SaveSession();

        _settingsService.VerifySet(s => s.OpenFiles = It.Is<List<SessionFile>>(files =>
            files.Count == 1 &&
            files[0].FilePath == retained.FileName &&
            files[0].IsUntitled));
        _fileSystemService.Verify(
            f => f.WriteAllTextAtomic(It.IsAny<string>(), "discard me"),
            Times.Never);
        _fileSystemService.Verify(
            f => f.WriteAllTextAtomic(It.IsAny<string>(), "restore me"),
            Times.Never);
        _settingsService.VerifySet(s => s.OpenFiles = It.Is<List<SessionFile>>(files =>
            files.Count == 1 &&
            files[0].Content == "restore me" &&
            !files[0].IsModified));
    }

    [Fact]
    public async Task ConfirmExit_EditDuringSave_ReturnsFalse()
    {
        var tab = CreateMockTab("test.txt", @"C:\test.txt");
        tab.Content = "snapshot";
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);
        tab.IsModified = true;
        _dialogService.Setup(d => d.ShowMessage(
                It.IsAny<string>(), It.IsAny<string>(),
                DialogButtons.YesNoCancel, DialogIcon.Warning))
            .Returns(Services.Interfaces.DialogResult.Yes);
        var writeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var writeCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _fileService.Setup(f => f.WriteFileWithEncodingAsync(
                tab.FilePath,
                "snapshot",
                It.IsAny<System.Text.Encoding>(),
                It.IsAny<bool>()))
            .Returns(() =>
            {
                writeStarted.SetResult();
                return writeCompletion.Task;
            });

        var confirmTask = _sut.ConfirmExitAsync();
        await writeStarted.Task;
        tab.Content = "newer edit";
        writeCompletion.SetResult();

        Assert.False(await confirmTask);
        Assert.True(tab.IsModified);
    }

    [Fact]
    public async Task ConfirmExit_SuccessfulBinarySaveDoesNotLookLikeConcurrentEdit()
    {
        var tempFile = Path.GetTempFileName();
        EditorTabViewModel? tab = null;
        try
        {
            await File.WriteAllBytesAsync(
                tempFile,
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            tab = CreateMockTab(Path.GetFileName(tempFile), tempFile);
            await tab.LoadFileAsync(tempFile);
            tab.ByteBuffer!.SetByte(7, 0x0B);
            _fileSystemService
                .Setup(service => service.WriteAllBytes(tempFile, It.IsAny<byte[]>()))
                .Callback<string, byte[]>((path, bytes) => File.WriteAllBytes(path, bytes));
            _sut.Tabs.Add(tab);
            _sut.SelectedTab = tab;
            _dialogService.Setup(d => d.ShowMessage(
                    It.IsAny<string>(), It.IsAny<string>(),
                    DialogButtons.YesNoCancel, DialogIcon.Warning))
                    .Returns(Services.Interfaces.DialogResult.Yes);

            Assert.True(await _sut.ConfirmExitAsync());
            Assert.False(tab.IsModified);
            Assert.Equal(0x0B, (await File.ReadAllBytesAsync(tempFile))[7]);
        }
        finally
        {
            tab?.Dispose();
            File.Delete(tempFile);
        }
    }

    // --- SaveSession ---

    [Fact]
    public void SaveSession_PersistsOpenTabs()
    {
        var tab = CreateMockTab("test.txt", @"C:\test.txt");
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);
        tab.FilePath = @"C:\test.txt";

        _sut.SaveSession();

        _settingsService.VerifySet(s => s.OpenFiles = It.Is<List<SessionFile>>(
            l => l.Count == 1 && l[0].FilePath == @"C:\test.txt"));
        _settingsService.Verify(s => s.Save(), Times.Once);
    }

    [Fact]
    public void SaveSession_UntitledTab_StoresContentInManifest()
    {
        var tab = CreateMockTab("Untitled-1");
        tab.Content = "hello world";
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);

        _sut.SaveSession();

        _settingsService.VerifySet(service =>
            service.OpenFiles = It.Is<List<SessionFile>>(files =>
                files.Count == 1 &&
                files[0].Content == "hello world"));
    }

    [Fact]
    public void SaveSession_DirtyUntitledTab_SnapshotsSilentlyForStartupRestore()
    {
        var tab = CreateMockTab("Untitled-1", isModified: true);
        tab.Content = "restore me";
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);

        _sut.SaveSession();

        _dialogService.Verify(
            service => service.ShowMessage(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DialogButtons>(),
                It.IsAny<DialogIcon>()),
            Times.Never);
        _settingsService.VerifySet(service =>
            service.OpenFiles = It.Is<List<SessionFile>>(files =>
                files.Count == 1 &&
                files[0].IsUntitled &&
                files[0].FilePath == "Untitled-1"));
        _settingsService.VerifySet(service =>
            service.OpenFiles = It.Is<List<SessionFile>>(files =>
                files.Count == 1 &&
                files[0].Content == "restore me" &&
                files[0].IsModified));
    }

    [Fact]
    public void SaveSession_DirtyNamedTab_SnapshotsWithoutOverwritingNamedFile()
    {
        var tab = CreateMockTab("notes.txt", @"C:\notes.txt", isModified: true);
        tab.Content = "unsaved buffer";
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);

        _sut.SaveSession();

        _dialogService.Verify(
            service => service.ShowMessage(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DialogButtons>(),
                It.IsAny<DialogIcon>()),
            Times.Never);
        _settingsService.VerifySet(service =>
            service.OpenFiles = It.Is<List<SessionFile>>(files =>
                files.Count == 1 &&
                !files[0].IsUntitled &&
                files[0].FilePath == @"C:\notes.txt" &&
                files[0].Content == "unsaved buffer" &&
                files[0].IsModified));
        _fileService.Verify(
            service => service.WriteAllTextAsync(
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public void SaveSession_SettingsFailure_DoesNotCleanPreviousSessionFiles()
    {
        var tab = CreateMockTab("Untitled-1");
        tab.Content = "hello world";
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);
        _settingsService.Setup(s => s.Save()).Throws(new IOException("disk full"));

        Assert.Throws<IOException>(() => _sut.SaveSession());

        _fileSystemService.Verify(
            f => f.GetFiles(It.IsAny<string>(), "*.tmp", false),
            Times.Never);
    }

    [Fact]
    public async Task SaveSession_DirtyNamedBinaryTab_PersistsValidatedOverlayWithoutWritingNamedFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            var original = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            File.WriteAllBytes(path, original);
            var tab = CreateMockTab(Path.GetFileName(path), path);
            await tab.LoadFileAsync(path);
            tab.ByteBuffer!.SetByte(7, 0xFF);
            _sut.Tabs.Add(tab);
            _sut.SelectedTab = tab;

            _sut.SaveSession();

            _settingsService.VerifySet(service =>
                service.OpenFiles = It.Is<List<SessionFile>>(files =>
                    files.Count == 1 &&
                    files[0].BinaryContentBase64 == null &&
                    files[0].BinaryBaseLength == original.LongLength &&
                    files[0].BinaryBaseSha256 ==
                    ComputeSha256(path) &&
                    HasSingleBinaryModification(files[0], 7, 0xFF) &&
                    files[0].IsModified));
            Assert.Equal(original, File.ReadAllBytes(path));
            tab.Dispose();
        }

        finally
        {
            File.Delete(path);
        }
    }

    private static bool HasSingleBinaryModification(
        SessionFile sessionFile,
        long offset,
        byte value)
    {
        return sessionFile.BinaryModifications != null &&
            sessionFile.BinaryModifications.Count == 1 &&
            sessionFile.BinaryModifications[0].Offset == offset &&
            sessionFile.BinaryModifications[0].Value == value;
    }

    [Fact]
    public async Task RestoreSession_DirtyNamedBinaryOverlay_RestoresExactBytesWithoutDiskWrite()
    {
        var path = Path.GetTempFileName();
        EditorTabViewModel? tab = null;
        try
        {
            var original = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            File.WriteAllBytes(path, original);
            var sessionFile = new SessionFile
            {
                EntryId = "binary",
                SnapshotVersion = 1,
                FilePath = path,
                IsBinaryMode = true,
                IsModified = true,
                BinaryBaseLength = original.LongLength,
                BinaryBaseSha256 = ComputeSha256(path),
                BinaryModifications =
                [
                    new BinaryModification { Offset = 7, Value = 0xFF }
                ]
            };
            _settingsService.Setup(service => service.OpenFiles)
                .Returns(new List<SessionFile> { sessionFile });
            _fileSystemService.Setup(service => service.FileExists(path)).Returns(true);
            _fileSystemService.Setup(service => service.GetFileSize(path)).Returns(original.LongLength);
            tab = CreateMockTab(Path.GetFileName(path));
            _tabFactory.Setup(factory => factory.CreateUntitled(null)).Returns(tab);

            await _sut.RestoreSessionAsync();

            Assert.Same(tab, Assert.Single(_sut.Tabs));
            Assert.Equal(0xFF, tab.ByteBuffer!.GetByte(7));
            Assert.True(tab.IsModified);
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally
        {
            tab?.Dispose();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RestoreSession_ChangedBinaryBase_RetainsEntryAsUnresolved()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            var sessionFile = new SessionFile
            {
                EntryId = "binary",
                SnapshotVersion = 1,
                FilePath = path,
                IsBinaryMode = true,
                IsModified = true,
                BinaryBaseLength = 3,
                BinaryBaseSha256 = new string('0', 64),
                BinaryModifications =
                [
                    new BinaryModification { Offset = 1, Value = 0xFF }
                ]
            };
            _settingsService.Setup(service => service.OpenFiles)
                .Returns(new List<SessionFile> { sessionFile });
            _fileSystemService.Setup(service => service.FileExists(path)).Returns(true);
            _fileSystemService.Setup(service => service.GetFileSize(path)).Returns(3);

            await _sut.RestoreSessionAsync();
            _sut.SaveSession();

            Assert.Empty(_sut.Tabs);
            Assert.True(_sut.HasUnresolvedSessionEntries);
            _settingsService.VerifySet(service =>
                service.OpenFiles = It.Is<List<SessionFile>>(files =>
                    files.Count == 1 &&
                    ReferenceEquals(files[0], sessionFile)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RestoreSession_PartialFailureThenSave_RetainsUnresolvedEntryPayload()
    {
        var unresolved = new SessionFile
        {
            EntryId = "unresolved",
            SnapshotVersion = 1,
            FilePath = @"C:\missing.bin",
            BinaryContentBase64 = "invalid-base64-payload",
            IsBinaryMode = true,
            IsModified = true
        };
        var restored = new SessionFile
        {
            EntryId = "restored",
            SnapshotVersion = 1,
            FilePath = "Untitled-1",
            IsUntitled = true,
            Content = "content",
            IsModified = true
        };
        _settingsService.Setup(service => service.OpenFiles)
            .Returns(new List<SessionFile> { unresolved, restored });
        var tab = CreateMockTab("Untitled-1");
        _tabFactory.Setup(factory => factory.CreateUntitled("content")).Returns(tab);

        await _sut.RestoreSessionAsync();
        _sut.SaveSession();

        _settingsService.VerifySet(service =>
            service.OpenFiles = It.Is<List<SessionFile>>(files =>
                files.Any(file =>
                    file.EntryId == "unresolved" &&
                    file.BinaryContentBase64 == unresolved.BinaryContentBase64)));
    }

    [Fact]
    public async Task SaveSession_UnresolvedEntrySurvivesLiveTabWithSameNamedPath()
    {
        const string path = @"C:\shared.txt";
        var unresolved = new SessionFile
        {
            EntryId = "unresolved",
            SnapshotVersion = 1,
            FilePath = path,
            BinaryContentBase64 = "invalid-base64-payload",
            IsBinaryMode = true,
            IsModified = true
        };
        _settingsService.Setup(service => service.OpenFiles)
            .Returns(new List<SessionFile> { unresolved });
        await _sut.RestoreSessionAsync();
        var live = CreateMockTab("shared.txt", path, isModified: true);
        live.SetContentBaseline("live", isModified: true);
        _sut.Tabs.Add(live);

        _sut.SaveSession();

        _settingsService.VerifySet(service =>
            service.OpenFiles = It.Is<List<SessionFile>>(files =>
                files.Count == 2 &&
                files.Any(file =>
                    file.EntryId == "unresolved" &&
                    file.FilePath == path &&
                    file.BinaryContentBase64 == "invalid-base64-payload") &&
                files.Any(file =>
                    file.EntryId != "unresolved" &&
                    file.FilePath == path &&
                    file.Content == "live")));
    }

    [Fact]
    public async Task SaveSession_ExplicitlyDiscardedUntitledTab_IsExcluded()
    {
        var tab = CreateMockTab("Untitled-1");
        tab.Content = "discard me";
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);
        _dialogService.Setup(d => d.ShowMessage(
                It.IsAny<string>(), It.IsAny<string>(),
                DialogButtons.YesNoCancel, DialogIcon.Warning))
            .Returns(Services.Interfaces.DialogResult.No);

        Assert.True(await _sut.PrepareForExitAsync());
        _sut.SaveSession();

        _settingsService.VerifySet(s => s.OpenFiles = It.Is<List<SessionFile>>(files => files.Count == 0));
        _fileSystemService.Verify(
            f => f.WriteAllTextAtomic(It.IsAny<string>(), "discard me"),
            Times.Never);
    }

    [Fact]
    public async Task CancelExitPreparation_RestoresUntitledSessionPersistence()
    {
        var tab = CreateMockTab("Untitled-1");
        tab.Content = "keep me";
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);
        _dialogService.Setup(d => d.ShowMessage(
                It.IsAny<string>(), It.IsAny<string>(),
                DialogButtons.YesNoCancel, DialogIcon.Warning))
            .Returns(Services.Interfaces.DialogResult.No);

        Assert.True(await _sut.PrepareForExitAsync());
        _sut.CancelExitPreparation();
        _sut.SaveSession();

        _settingsService.VerifySet(s => s.OpenFiles = It.Is<List<SessionFile>>(
            files => files.Count == 1 && files[0].IsUntitled));
    }

    [Fact]
    public async Task SaveSession_DiscardedTabDoesNotShiftPersistedActiveIndex()
    {
        var discarded = CreateMockTab("Untitled-1");
        var selected = CreateMockTab("selected.txt", @"C:\selected.txt");
        var other = CreateMockTab("other.txt", @"C:\other.txt");
        discarded.Content = "discard";
        _sut.Tabs.Add(discarded);
        _sut.Tabs.Add(selected);
        _sut.Tabs.Add(other);
        _sut.SelectedTab = selected;
        _dialogService.Setup(d => d.ShowMessage(
                It.IsAny<string>(), It.IsAny<string>(),
                DialogButtons.YesNoCancel, DialogIcon.Warning))
            .Returns(Services.Interfaces.DialogResult.No);

        Assert.True(await _sut.PrepareForExitAsync());
        _sut.SaveSession();

        _settingsService.VerifySet(s => s.ActiveTabIndex = 0);
    }

    [Fact]
    public async Task LoadSession_RestoresFileTab_Asynchronously()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "content");
            var session = new SessionData
            {
                ActiveTabIndex = 0,
                Files =
                {
                    new SessionFileEntry { FilePath = tempFile, IsUntitled = false }
                }
            };
            _workspaceService.Setup(w => w.LoadNamedSession("saved")).Returns(session);
            _fileSystemService.Setup(f => f.FileExists(tempFile)).Returns(true);

            var tab = CreateMockTab("test.txt", tempFile);
            _tabFactory.Setup(f => f.Create()).Returns(tab);
            _fileService.Setup(f => f.ReadFileWithEncodingAsync(tempFile))
                .ReturnsAsync(new FileReadResult("content", System.Text.Encoding.UTF8, false));

            await _sut.LoadSessionCommand.ExecuteAsync("saved");

            Assert.Single(_sut.Tabs);
            Assert.Equal(tab, _sut.SelectedTab);
            Assert.Equal("Session loaded: saved", _sut.StatusText);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadSession_CancelledReplacement_KeepsCurrentWorkspace()
    {
        var current = CreateMockTab("current.txt", isModified: true);
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(current);
        _sut.NewFileCommand.Execute(null);
        current.IsModified = true;
        _workspaceService.Setup(w => w.LoadNamedSession("saved"))
            .Returns(new SessionData());
        _dialogService.Setup(d => d.ShowMessage(
                It.IsAny<string>(), It.IsAny<string>(),
                DialogButtons.YesNoCancel, DialogIcon.Warning))
            .Returns(Services.Interfaces.DialogResult.Cancel);

        await _sut.LoadSessionCommand.ExecuteAsync("saved");

        Assert.Same(current, Assert.Single(_sut.Tabs));
        Assert.Same(current, _sut.SelectedTab);
        _tabFactory.Verify(f => f.Create(), Times.Never);
    }

    [Fact]
    public async Task LoadSession_SaveFailure_KeepsCurrentWorkspace()
    {
        var current = CreateMockTab("current.txt", @"C:\current.txt");
        current.Content = "unsaved";
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(current);
        _sut.NewFileCommand.Execute(null);
        current.IsModified = true;
        _workspaceService.Setup(w => w.LoadNamedSession("saved"))
            .Returns(new SessionData());
        _dialogService.Setup(d => d.ShowMessage(
                It.IsAny<string>(), It.IsAny<string>(),
                DialogButtons.YesNoCancel, DialogIcon.Warning))
            .Returns(Services.Interfaces.DialogResult.Yes);
        _fileService.Setup(f => f.WriteFileWithEncodingAsync(
                current.FilePath,
                current.Content,
                It.IsAny<System.Text.Encoding>(),
                It.IsAny<bool>()))
            .ThrowsAsync(new IOException("disk full"));

        await _sut.LoadSessionCommand.ExecuteAsync("saved");

        Assert.Same(current, Assert.Single(_sut.Tabs));
        Assert.Same(current, _sut.SelectedTab);
        Assert.Contains("Error saving", _sut.StatusText);
    }

    [Fact]
    public async Task LoadSession_DiscardChoiceIsNotPromptedTwiceWithoutNewEdits()
    {
        var current = CreateMockTab("current.txt", isModified: true);
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(current);
        _sut.NewFileCommand.Execute(null);
        current.IsModified = true;
        _workspaceService.Setup(w => w.LoadNamedSession("saved"))
            .Returns(new SessionData());
        _dialogService.Setup(d => d.ShowMessage(
                It.IsAny<string>(), It.IsAny<string>(),
                DialogButtons.YesNoCancel, DialogIcon.Warning))
            .Returns(Services.Interfaces.DialogResult.No);

        await _sut.LoadSessionCommand.ExecuteAsync("saved");

        Assert.Empty(_sut.Tabs);
        _dialogService.Verify(d => d.ShowMessage(
            It.IsAny<string>(), It.IsAny<string>(),
            DialogButtons.YesNoCancel, DialogIcon.Warning), Times.Once);
    }

    [Fact]
    public async Task LoadSession_StagingFailure_KeepsCurrentWorkspace()
    {
        var current = CreateMockTab("current.txt");
        current.SetContentBaseline("keep", isModified: false);
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(current);
        _sut.NewFileCommand.Execute(null);
        var tempFile = Path.GetTempFileName();
        try
        {
            var session = new SessionData
            {
                Files = { new SessionFileEntry { FilePath = tempFile } }
            };
            _workspaceService.Setup(w => w.LoadNamedSession("saved")).Returns(session);
            _fileSystemService.Setup(f => f.FileExists(tempFile)).Returns(true);
            var staged = CreateMockTab("staged.txt");
            _tabFactory.Setup(f => f.Create()).Returns(staged);
            _fileService.Setup(f => f.ReadFileWithEncodingAsync(tempFile))
                .ThrowsAsync(new IOException("read failed"));

            await _sut.LoadSessionCommand.ExecuteAsync("saved");

            Assert.Same(current, Assert.Single(_sut.Tabs));
            Assert.Same(current, _sut.SelectedTab);
            Assert.Equal("keep", current.Content);
            Assert.Contains("Failed to load session", _sut.StatusText);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadSession_EditDuringStaging_ReconfirmsBeforeReplacement()
    {
        var current = CreateMockTab("current.txt");
        current.SetContentBaseline("original", isModified: false);
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(current);
        _sut.NewFileCommand.Execute(null);
        var tempFile = Path.GetTempFileName();
        try
        {
            var loadStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var loadCompletion = new TaskCompletionSource<FileReadResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _workspaceService.Setup(w => w.LoadNamedSession("saved"))
                .Returns(new SessionData
                {
                    Files = { new SessionFileEntry { FilePath = tempFile } }
                });
            _fileSystemService.Setup(f => f.FileExists(tempFile)).Returns(true);
            _tabFactory.Setup(f => f.Create()).Returns(CreateMockTab("staged.txt"));
            _fileService.Setup(f => f.ReadFileWithEncodingAsync(tempFile))
                .Returns(() =>
                {
                    loadStarted.SetResult();
                    return loadCompletion.Task;
                });
            _dialogService.Setup(d => d.ShowMessage(
                    It.IsAny<string>(), It.IsAny<string>(),
                    DialogButtons.YesNoCancel, DialogIcon.Warning))
                .Returns(Services.Interfaces.DialogResult.Cancel);

            var loadTask = _sut.LoadSessionCommand.ExecuteAsync("saved");
            await loadStarted.Task;
            current.Content = "edited while loading";
            loadCompletion.SetResult(new FileReadResult(
                "replacement",
                System.Text.Encoding.UTF8,
                false));
            await loadTask;

            Assert.Same(current, Assert.Single(_sut.Tabs));
            Assert.Equal("edited while loading", current.Content);
            Assert.True(current.IsModified);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void OpenFileIdentity_NormalizesPathsButPreservesCaseVariants()
    {
        Assert.True(MainViewModel.HasSameOpenIdentity(
            @".\folder\..\file.txt",
            @".\file.txt"));
        Assert.False(MainViewModel.HasSameOpenIdentity(
            @"C:\CaseSensitive\File.txt",
            @"C:\CaseSensitive\file.txt"));
    }

    [Fact]
    public void AutoSaveIds_SavedCaseVariantsUseStableCollisionFreeTabIdentity()
    {
        var first = CreateMockTab("File.txt", @"C:\CaseSensitive\File.txt", isModified: true);
        var second = CreateMockTab("file.txt", @"C:\CaseSensitive\file.txt", isModified: true);
        _sut.Tabs.Add(first);
        _sut.Tabs.Add(second);

        var entriesBefore = _sut.GetAutoSaveEntries();
        var entriesAfter = _sut.GetAutoSaveEntries();

        Assert.Equal(2, entriesBefore.Count);
        Assert.Equal(2, entriesBefore.Select(entry => entry.Id).Distinct().Count());
        Assert.Equal(
            entriesBefore.Select(entry => entry.Id),
            entriesAfter.Select(entry => entry.Id));
        Assert.Contains(entriesBefore, entry =>
            entry.FilePath == MainViewModel.NormalizeFilePath(first.FilePath));
        Assert.Contains(entriesBefore, entry =>
            entry.FilePath == MainViewModel.NormalizeFilePath(second.FilePath));
    }

    [Fact]
    public void AutoSaveIds_UntitledTabsRemainStableAcrossReordering()
    {
        var first = CreateMockTab("first");
        var second = CreateMockTab("second");
        _tabFactory.SetupSequence(f => f.CreateUntitled(null))
            .Returns(first)
            .Returns(second);
        _sut.NewFileCommand.Execute(null);
        _sut.NewFileCommand.Execute(null);
        first.Content = "one";
        second.Content = "two";
        var idsBefore = _sut.GetAutoSaveEntries()
            .ToDictionary(entry => entry.Content, entry => entry.Id);

        _sut.Tabs.Move(0, 1);
        var idsAfter = _sut.GetAutoSaveEntries()
            .ToDictionary(entry => entry.Content, entry => entry.Id);

        Assert.Equal(idsBefore["one"], idsAfter["one"]);
        Assert.Equal(idsBefore["two"], idsAfter["two"]);
        Assert.NotEqual(idsBefore["one"], idsBefore["two"]);
    }

    [Fact]
    public void RecoverTab_MarksRecoveredContentModified()
    {
        var tab = CreateMockTab("recovered.txt");
        _tabFactory.Setup(f => f.CreateUntitled("recovered")).Returns(tab);

        var recovered = _sut.RecoverTab(new AutoSaveEntry(
            "id", "recovered.txt", @"C:\recovered.txt", "recovered", false));

        Assert.True(recovered.IsModified);
        Assert.Equal(@"C:\recovered.txt", recovered.FilePath);
    }

    [Fact]
    public async Task AutoSave_DirtyBinaryRoundTripsWithoutWritingNamedFile()
    {
        var path = Path.GetTempFileName();
        EditorTabViewModel? source = null;
        EditorTabViewModel? recovered = null;
        try
        {
            var original = new byte[]
            {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
            };
            await File.WriteAllBytesAsync(path, original);
            source = CreateMockTab(Path.GetFileName(path), path);
            await source.LoadFileAsync(path);
            source.ByteBuffer!.SetByte(7, 0xFF);
            _sut.Tabs.Add(source);
            var entry = Assert.Single(_sut.GetAutoSaveEntries());
            recovered = CreateMockTab(Path.GetFileName(path));
            _tabFactory.Setup(factory => factory.CreateUntitled(null))
                .Returns(recovered);

            recovered = _sut.RecoverTab(entry);

            Assert.True(entry.IsBinaryMode);
            Assert.Equal(0xFF, recovered.ByteBuffer!.GetByte(7));
            Assert.True(recovered.IsModified);
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            source?.Dispose();
            recovered?.Dispose();
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(1200, true)]
    [InlineData(1252, false)]
    public async Task AutoSave_DirtyNamedTextRoundTripsFormatForExplicitSave(
        int codePage,
        bool hasBom)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        const string path = @"C:\encoded.txt";
        var source = CreateMockTab("encoded.txt", path);
        source.RestoreTextSnapshot(
            "restored text",
            "encoded.txt",
            path,
            codePage,
            hasBom,
            isModified: true);
        _sut.Tabs.Add(source);
        var entry = Assert.Single(_sut.GetAutoSaveEntries());
        var recovered = CreateMockTab("encoded.txt");
        _tabFactory.Setup(factory => factory.CreateUntitled("restored text"))
            .Returns(recovered);

        recovered = _sut.RecoverTab(entry);
        await recovered.SaveCommand.ExecuteAsync(null);

        _fileService.Verify(service => service.WriteFileWithEncodingAsync(
            path,
            "restored text",
            It.Is<Encoding>(encoding => encoding.CodePage == codePage),
            hasBom), Times.Once);
    }

    [Fact]
    public async Task RecoverThenRestoreSession_ExactStableEntryIsAdoptedOnce()
    {
        var source = CreateMockTab("Untitled-1", isModified: true);
        source.Content = "same";
        _sut.Tabs.Add(source);
        var entry = Assert.Single(_sut.GetAutoSaveEntries());
        _sut.Tabs.Clear();
        var recovered = CreateMockTab("Untitled-1");
        _tabFactory.Setup(factory => factory.CreateUntitled("same"))
            .Returns(recovered);

        var recovery = _sut.RecoverTabs(new[] { entry });
        _settingsService.Setup(service => service.OpenFiles)
            .Returns(new List<SessionFile>
            {
                new()
                {
                    EntryId = entry.SessionEntryId,
                    SnapshotVersion = entry.SnapshotVersion,
                    FilePath = "Untitled-1",
                    IsUntitled = true,
                    Content = "same",
                    TextContentBase64 = entry.TextContentBase64,
                    IsModified = true,
                    EncodingCodePage = entry.EncodingCodePage,
                    HasBom = entry.HasBom,
                    BytesPerRow = entry.BytesPerRow,
                    LargeFileTopLine = entry.LargeFileTopLine
                }
            });

        await _sut.RestoreSessionAsync();

        Assert.True(recovery.Success);
        Assert.Same(recovered, Assert.Single(_sut.Tabs));
        Assert.Equal(entry.TabIdentity, recovered.AutoSaveIdentity);
        _tabFactory.Verify(factory => factory.CreateUntitled("same"), Times.Once);
    }

    [Fact]
    public void RecoverTabs_PartialFailureReportsFailureAndKeepsRecoveredTabs()
    {
        var first = CreateMockTab("one.txt");
        _tabFactory.Setup(f => f.CreateUntitled("one")).Returns(first);
        _tabFactory.Setup(f => f.CreateUntitled("two"))
            .Throws(new InvalidOperationException("restore failed"));
        var entries = new[]
        {
            new AutoSaveEntry("one", "one.txt", null, "one", true),
            new AutoSaveEntry("two", "two.txt", null, "two", true)
        };

        var recovery = _sut.RecoverTabs(entries);

        Assert.False(recovery.Success);
        Assert.Equal(new[] { "one" }, recovery.RecoveredEntryIds);
        Assert.Same(first, Assert.Single(_sut.Tabs));
        Assert.Contains("recovery files were retained", _sut.StatusText);
    }

    [Fact]
    public void RecoverTabs_ExactStableDuplicatesAreAdoptedOnce()
    {
        const string payload = "cwBhAG0AZQA=";
        var first = new AutoSaveEntry(
            "generation-one",
            "Untitled-1",
            null,
            "same",
            true)
        {
            SnapshotVersion = 2,
            SessionEntryId = "stable-entry",
            TabIdentity = "stable-tab",
            IsModified = true,
            TextContentBase64 = payload
        };
        var second = first with { Id = "generation-two" };
        var tab = CreateMockTab("Untitled-1");
        _tabFactory.Setup(factory => factory.CreateUntitled("same"))
            .Returns(tab);

        var recovery = _sut.RecoverTabs(new[] { first, second });

        Assert.True(recovery.Success);
        Assert.Equal(
            new[] { "generation-one", "generation-two" },
            recovery.RecoveredEntryIds);
        Assert.Same(tab, Assert.Single(_sut.Tabs));
        _tabFactory.Verify(factory => factory.CreateUntitled("same"), Times.Once);
    }

    [Fact]
    public void RecoverTab_FailedCandidateIsDisposedBeforeFailureEscapes()
    {
        var candidate = CreateMockTab("candidate.bin");
        candidate.RestoreBinarySnapshot(
            new byte[] { 1, 2, 3 },
            "candidate.bin",
            filePath: null,
            isModified: true);
        _tabFactory.Setup(factory => factory.CreateUntitled(null))
            .Returns(candidate);
        var entry = new AutoSaveEntry(
            "broken",
            "candidate.bin",
            null,
            "",
            true)
        {
            SnapshotVersion = 2,
            IsBinaryMode = true,
            IsModified = true,
            BinaryContentBase64 = "not-base64"
        };

        Assert.Throws<FormatException>(() => _sut.RecoverTab(entry));

        Assert.Null(candidate.ByteBuffer);
    }

    // --- RestoreSession ---

    [Fact]
    public async Task RestoreSession_RestoresFileTab()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "content");
            var sessionFiles = new List<SessionFile>
            {
                new() { FilePath = tempFile, IsUntitled = false }
            };
            _settingsService.Setup(s => s.OpenFiles).Returns(sessionFiles);
            _settingsService.Setup(s => s.ActiveTabIndex).Returns(0);
            _fileSystemService.Setup(f => f.FileExists(tempFile)).Returns(true);

            var tab = CreateMockTab("test.txt", tempFile);
            _tabFactory.Setup(f => f.Create()).Returns(tab);
            _fileService.Setup(f => f.ReadFileWithEncodingAsync(tempFile))
                .ReturnsAsync(new FileReadResult("content", System.Text.Encoding.UTF8, false));

            await _sut.RestoreSessionAsync();

            Assert.Single(_sut.Tabs);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RestoreSession_RestoresDirtyNamedSnapshotOverDiskContent()
    {
        var filePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(filePath, "disk content");
            var sessionFiles = new List<SessionFile>
            {
                new()
                {
                    FilePath = filePath,
                    IsUntitled = false,
                    Content = "unsaved buffer"
                }
            };
            _settingsService.Setup(service => service.OpenFiles).Returns(sessionFiles);
            _settingsService.Setup(service => service.ActiveTabIndex).Returns(0);
            _fileSystemService.Setup(service => service.FileExists(filePath)).Returns(true);
            var tab = CreateMockTab("notes.txt");
            _tabFactory.Setup(factory => factory.CreateUntitled("unsaved buffer"))
                .Returns(tab);

            await _sut.RestoreSessionAsync();

            Assert.Same(tab, Assert.Single(_sut.Tabs));
            Assert.Equal(filePath, tab.FilePath);
            Assert.Equal("unsaved buffer", tab.Content);
            Assert.True(tab.IsModified);
            Assert.Equal(FileOpenMode.Text, tab.Mode);
            _fileService.Verify(
                service => service.ReadFileWithEncodingAsync(filePath),
                Times.Never);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task RestoreSession_RestoresDirtyNamedSnapshotWhenFileIsMissing()
    {
        const string filePath = @"C:\missing-notes.txt";
        var sessionFiles = new List<SessionFile>
        {
            new()
            {
                FilePath = filePath,
                IsUntitled = false,
                Content = "unsaved buffer"
            }
        };
        _settingsService.Setup(service => service.OpenFiles).Returns(sessionFiles);
        _fileSystemService.Setup(service => service.FileExists(filePath)).Returns(false);
        var tab = CreateMockTab("missing-notes.txt");
        _tabFactory.Setup(factory => factory.CreateUntitled("unsaved buffer"))
            .Returns(tab);

        await _sut.RestoreSessionAsync();

        Assert.Same(tab, Assert.Single(_sut.Tabs));
        Assert.Equal(filePath, tab.FilePath);
        Assert.Equal("unsaved buffer", tab.Content);
        Assert.True(tab.IsModified);
    }

    [Fact]
    public async Task RestoreSession_RestoresUntitledTab()
    {
        var sessionFiles = new List<SessionFile>
        {
            new() { FilePath = "Untitled-1", IsUntitled = true, Content = "saved content" }
        };
        _settingsService.Setup(s => s.OpenFiles).Returns(sessionFiles);
        _settingsService.Setup(s => s.ActiveTabIndex).Returns(0);

        var tab = CreateMockTab("Untitled-1");
        _tabFactory.Setup(f => f.CreateUntitled("saved content")).Returns(tab);

        await _sut.RestoreSessionAsync();

        Assert.Single(_sut.Tabs);
    }

    [Fact]
    public async Task RestoreSession_SkipsUntitledBinaryTabs()
    {
        var sessionFiles = new List<SessionFile>
        {
            new() { FilePath = "Untitled-1", IsUntitled = true, IsBinaryMode = true }
        };
        _settingsService.Setup(s => s.OpenFiles).Returns(sessionFiles);

        await _sut.RestoreSessionAsync();

        Assert.Empty(_sut.Tabs);
    }

    [Fact]
    public async Task RestoreSession_SkipsMissingFiles()
    {
        var sessionFiles = new List<SessionFile>
        {
            new() { FilePath = @"C:\missing.txt", IsUntitled = false }
        };
        _settingsService.Setup(s => s.OpenFiles).Returns(sessionFiles);
        _fileSystemService.Setup(f => f.FileExists(@"C:\missing.txt")).Returns(false);

        await _sut.RestoreSessionAsync();

        Assert.Empty(_sut.Tabs);
    }

    [Fact]
    public async Task RestoreSession_LoadFailure_LogsWarningAndSkipsTab()
    {
        using var trace = new TraceCapture();
        const string filePath = @"C:\broken.txt";
        var sessionFiles = new List<SessionFile>
        {
            new() { FilePath = filePath, IsUntitled = false }
        };
        _settingsService.Setup(s => s.OpenFiles).Returns(sessionFiles);
        _fileSystemService.Setup(f => f.FileExists(filePath)).Returns(true);

        var tab = CreateMockTab("broken.txt", filePath);
        _tabFactory.Setup(f => f.Create()).Returns(tab);
        _fileService.Setup(f => f.ReadFileWithEncodingAsync(filePath))
            .ThrowsAsync(new IOException("read failed"));

        await _sut.RestoreSessionAsync();
        System.Diagnostics.Trace.Flush();

        Assert.Empty(_sut.Tabs);
        Assert.Contains("Failed to restore session file", trace.Messages);
        Assert.Contains(filePath, trace.Messages);
    }

    [Fact]
    public async Task RestoreSession_SkippedEntryBeforeActive_PreservesSelectionAndState()
    {
        var skipped = new SessionFile
        {
            EntryId = "skipped",
            FilePath = @"C:\missing.txt"
        };
        var active = new SessionFile
        {
            EntryId = "active",
            SnapshotVersion = 1,
            FilePath = "Untitled-2",
            IsUntitled = true,
            Content = string.Empty,
            IsModified = false,
            CursorOffset = 42,
            ScrollOffset = 17.5,
            HexOffset = 96,
            HexScrollOffset = 3,
            BytesPerRow = 24
        };
        _settingsService.Setup(service => service.OpenFiles)
            .Returns(new List<SessionFile> { skipped, active });
        _settingsService.Setup(service => service.ActiveSessionEntryId).Returns("active");
        _fileSystemService.Setup(service => service.FileExists(skipped.FilePath)).Returns(false);
        var tab = CreateMockTab("Untitled-2", isModified: true);
        _tabFactory.Setup(factory => factory.CreateUntitled(string.Empty)).Returns(tab);

        await _sut.RestoreSessionAsync();

        Assert.Same(tab, _sut.SelectedTab);
        Assert.False(tab.IsModified);
        Assert.Equal(42, tab.CursorOffset);
        Assert.Equal(17.5, tab.ScrollOffset);
        Assert.Equal(96, tab.HexOffset);
        Assert.Equal(3, tab.HexScrollOffset);
        Assert.Equal(24, tab.BytesPerRow);
    }

    [Fact]
    public async Task RestoreSession_FailedActiveEntry_SelectsNextRestoredSource()
    {
        var failed = new SessionFile
        {
            EntryId = "failed",
            FilePath = @"C:\missing.txt"
        };
        var next = new SessionFile
        {
            EntryId = "next",
            SnapshotVersion = 1,
            FilePath = "Untitled-2",
            IsUntitled = true,
            Content = "next"
        };
        _settingsService.Setup(service => service.OpenFiles)
            .Returns(new List<SessionFile> { failed, next });
        _settingsService.Setup(service => service.ActiveSessionEntryId).Returns("failed");
        _fileSystemService.Setup(service => service.FileExists(failed.FilePath)).Returns(false);
        var nextTab = CreateMockTab("Untitled-2");
        _tabFactory.Setup(factory => factory.CreateUntitled("next")).Returns(nextTab);

        await _sut.RestoreSessionAsync();

        Assert.Same(nextTab, _sut.SelectedTab);
    }

    [Theory]
    [InlineData("bom", "\uFEFFleading")]
    [InlineData("high", "\uD800")]
    [InlineData("low", "\uDC00")]
    [InlineData("controls", "a\0b\r\nc")]
    public async Task RestoreSession_LosslessTextPayload_PreservesUtf16CodeUnits(
        string _,
        string content)
    {
        var bytes = new byte[content.Length * sizeof(char)];
        for (var index = 0; index < content.Length; index++)
        {
            bytes[index * 2] = (byte)content[index];
            bytes[(index * 2) + 1] = (byte)(content[index] >> 8);
        }

        var sessionFile = new SessionFile
        {
            EntryId = "exact-text",
            SnapshotVersion = 2,
            FilePath = "Untitled-1",
            IsUntitled = true,
            TextContentBase64 = Convert.ToBase64String(bytes),
            IsModified = true
        };
        _settingsService.Setup(service => service.OpenFiles)
            .Returns(new List<SessionFile> { sessionFile });
        var tab = CreateMockTab("Untitled-1");
        _tabFactory.Setup(factory => factory.CreateUntitled(
                It.Is<string>(value => value == content)))
            .Returns(tab);

        await _sut.RestoreSessionAsync();

        Assert.Equal(content, Assert.Single(_sut.Tabs).Content);
    }

    [Theory]
    [InlineData("bom", "\uFEFFleading")]
    [InlineData("high", "\uD800")]
    [InlineData("low", "\uDC00")]
    [InlineData("controls", "a\0b\r\nc")]
    public async Task RestoreSession_LegacyUtf8Payload_DoesNotStripLeadingBom(
        string _,
        string content)
    {
        const string tempPath = @"C:\legacy.tmp";
        var sessionFile = new SessionFile
        {
            EntryId = "legacy-text",
            FilePath = "Untitled-1",
            IsUntitled = true,
            TempFilePath = tempPath,
            IsModified = true
        };
        _settingsService.Setup(service => service.OpenFiles)
            .Returns(new List<SessionFile> { sessionFile });
        _fileSystemService.Setup(service => service.FileExists(tempPath)).Returns(true);
        var legacyBytes = content.SelectMany(character =>
        {
            var codeUnit = (int)character;
            if (codeUnit <= 0x7F)
                return new[] { (byte)codeUnit };
            if (codeUnit <= 0x7FF)
            {
                return new[]
                {
                    (byte)(0xC0 | (codeUnit >> 6)),
                    (byte)(0x80 | (codeUnit & 0x3F))
                };
            }

            return new[]
            {
                (byte)(0xE0 | (codeUnit >> 12)),
                (byte)(0x80 | ((codeUnit >> 6) & 0x3F)),
                (byte)(0x80 | (codeUnit & 0x3F))
            };
        }).ToArray();
        _fileSystemService.Setup(service => service.ReadAllBytesAsync(tempPath))
            .ReturnsAsync(legacyBytes);
        var tab = CreateMockTab("Untitled-1");
        _tabFactory.Setup(factory => factory.CreateUntitled(content)).Returns(tab);

        await _sut.RestoreSessionAsync();

        Assert.Equal(content, Assert.Single(_sut.Tabs).Content);
    }

    [Fact]
    public async Task RestoreSession_DistinctNamedSnapshotsWithSamePathBothSurvive()
    {
        const string path = @"C:\notes.txt";
        var first = new SessionFile
        {
            EntryId = "first",
            SnapshotVersion = 1,
            FilePath = path,
            Content = "first",
            IsModified = true
        };
        var duplicate = new SessionFile
        {
            EntryId = "duplicate",
            SnapshotVersion = 1,
            FilePath = path,
            Content = "duplicate",
            IsModified = true
        };
        _settingsService.Setup(service => service.OpenFiles)
            .Returns(new List<SessionFile> { first, duplicate });
        _settingsService.Setup(service => service.ActiveSessionEntryId).Returns("duplicate");
        var firstTab = CreateMockTab("notes.txt");
        var secondTab = CreateMockTab("notes.txt");
        _tabFactory.Setup(factory => factory.CreateUntitled("first")).Returns(firstTab);
        _tabFactory.Setup(factory => factory.CreateUntitled("duplicate")).Returns(secondTab);

        await _sut.RestoreSessionAsync();
        _sut.SaveSession();

        Assert.Equal(new[] { firstTab, secondTab }, _sut.Tabs);
        Assert.Same(secondTab, _sut.SelectedTab);
        _settingsService.VerifySet(service =>
            service.OpenFiles = It.Is<List<SessionFile>>(files =>
                files.Count == 2 &&
                files[0].EntryId == "first" &&
                files[0].Content == "first" &&
                files[1].EntryId == "duplicate" &&
                files[1].Content == "duplicate"));
    }

    [Fact]
    public async Task RestoreSession_DistinctSameNameUntitledEntriesBothSurvive()
    {
        var first = new SessionFile
        {
            EntryId = "first",
            SnapshotVersion = 1,
            FilePath = "Untitled-1",
            IsUntitled = true,
            Content = "one",
            IsModified = true
        };
        var second = new SessionFile
        {
            EntryId = "second",
            SnapshotVersion = 1,
            FilePath = "Untitled-1",
            IsUntitled = true,
            Content = "two",
            IsModified = true
        };
        _settingsService.Setup(service => service.OpenFiles)
            .Returns(new List<SessionFile> { first, second });
        var firstTab = CreateMockTab("Untitled-1");
        var secondTab = CreateMockTab("Untitled-1");
        _tabFactory.Setup(factory => factory.CreateUntitled("one")).Returns(firstTab);
        _tabFactory.Setup(factory => factory.CreateUntitled("two")).Returns(secondTab);

        await _sut.RestoreSessionAsync();

        Assert.Equal(new[] { firstTab, secondTab }, _sut.Tabs);
        Assert.Equal("one", firstTab.Content);
        Assert.Equal("two", secondTab.Content);
    }

    [Fact]
    public async Task RestoreSession_CleanNamedSnapshotWithChangedBaseBecomesModified()
    {
        const string path = @"C:\notes.txt";
        var sessionFile = new SessionFile
        {
            EntryId = "clean",
            SnapshotVersion = 1,
            FilePath = path,
            Content = "snapshot",
            IsModified = false,
            TextBaseLength = 4,
            TextBaseSha256 = new string('0', 64)
        };
        _settingsService.Setup(service => service.OpenFiles)
            .Returns(new List<SessionFile> { sessionFile });
        _fileSystemService.Setup(service => service.FileExists(path)).Returns(true);
        _fileSystemService.Setup(service => service.OpenRead(path))
            .Returns(() => new MemoryStream("disk"u8.ToArray()));
        var tab = CreateMockTab("notes.txt");
        _tabFactory.Setup(factory => factory.CreateUntitled("snapshot")).Returns(tab);

        await _sut.RestoreSessionAsync();

        Assert.True(Assert.Single(_sut.Tabs).IsModified);
    }

    [Fact]
    public async Task RestoreSession_CleanNamedSnapshotWithMatchingBaseRemainsClean()
    {
        const string path = @"C:\notes.txt";
        var diskBytes = "disk"u8.ToArray();
        var sessionFile = new SessionFile
        {
            EntryId = "clean",
            SnapshotVersion = 1,
            FilePath = path,
            Content = "disk",
            IsModified = false,
            TextBaseLength = diskBytes.LongLength,
            TextBaseSha256 = Convert.ToHexString(SHA256.HashData(diskBytes))
        };
        _settingsService.Setup(service => service.OpenFiles)
            .Returns(new List<SessionFile> { sessionFile });
        _fileSystemService.Setup(service => service.FileExists(path)).Returns(true);
        _fileSystemService.Setup(service => service.OpenRead(path))
            .Returns(() => new MemoryStream(diskBytes));
        var tab = CreateMockTab("notes.txt", isModified: true);
        _tabFactory.Setup(factory => factory.CreateUntitled("disk")).Returns(tab);

        await _sut.RestoreSessionAsync();

        Assert.False(Assert.Single(_sut.Tabs).IsModified);
    }

    [Fact]
    public async Task RestoreSession_CleanNamedSnapshotWithStalePayloadBecomesModified()
    {
        const string path = @"C:\notes.txt";
        var diskBytes = "disk"u8.ToArray();
        var sessionFile = new SessionFile
        {
            EntryId = "clean",
            SnapshotVersion = 1,
            FilePath = path,
            Content = "stale snapshot",
            IsModified = false,
            TextBaseLength = diskBytes.LongLength,
            TextBaseSha256 = Convert.ToHexString(SHA256.HashData(diskBytes))
        };
        _settingsService.Setup(service => service.OpenFiles)
            .Returns(new List<SessionFile> { sessionFile });
        _fileSystemService.Setup(service => service.FileExists(path)).Returns(true);
        _fileSystemService.Setup(service => service.OpenRead(path))
            .Returns(() => new MemoryStream(diskBytes));
        var tab = CreateMockTab("notes.txt");
        _tabFactory.Setup(factory => factory.CreateUntitled("stale snapshot"))
            .Returns(tab);

        await _sut.RestoreSessionAsync();

        Assert.True(Assert.Single(_sut.Tabs).IsModified);
    }

    [Fact]
    public async Task RestoreSession_CleanNamedSnapshotWithMissingBaseBecomesModified()
    {
        const string path = @"C:\missing-notes.txt";
        var sessionFile = new SessionFile
        {
            EntryId = "clean",
            SnapshotVersion = 1,
            FilePath = path,
            Content = "snapshot",
            IsModified = false,
            TextBaseLength = 8,
            TextBaseSha256 = new string('A', 64)
        };
        _settingsService.Setup(service => service.OpenFiles)
            .Returns(new List<SessionFile> { sessionFile });
        _fileSystemService.Setup(service => service.FileExists(path)).Returns(false);
        var tab = CreateMockTab("missing-notes.txt");
        _tabFactory.Setup(factory => factory.CreateUntitled("snapshot")).Returns(tab);

        await _sut.RestoreSessionAsync();

        Assert.True(Assert.Single(_sut.Tabs).IsModified);
    }

    [Fact]
    public async Task RestoreSession_RepeatedStableIdRequiresExactPayloadMatch()
    {
        List<SessionFile> source =
        [
            new()
            {
                EntryId = "stable",
                SnapshotVersion = 1,
                FilePath = "Untitled-1",
                IsUntitled = true,
                Content = "first",
                IsModified = true
            }
        ];
        _settingsService.Setup(service => service.OpenFiles).Returns(() => source);
        var firstTab = CreateMockTab("Untitled-1");
        var secondTab = CreateMockTab("Untitled-1");
        _tabFactory.Setup(factory => factory.CreateUntitled("first")).Returns(firstTab);
        _tabFactory.Setup(factory => factory.CreateUntitled("second")).Returns(secondTab);
        await _sut.RestoreSessionAsync();
        source =
        [
            new()
            {
                EntryId = "stable",
                SnapshotVersion = 1,
                FilePath = "Untitled-1",
                IsUntitled = true,
                Content = "second",
                IsModified = true
            }
        ];

        await _sut.RestoreSessionAsync();

        Assert.Equal(new[] { firstTab, secondTab }, _sut.Tabs);
        Assert.NotEqual("stable", source[0].EntryId);
    }

    [Fact]
    public async Task RestoreSession_RepeatedExactStableEntryReusesTab()
    {
        var sessionFile = new SessionFile
        {
            EntryId = "stable",
            SnapshotVersion = 1,
            FilePath = "Untitled-1",
            IsUntitled = true,
            Content = "same",
            IsModified = true
        };
        _settingsService.Setup(service => service.OpenFiles)
            .Returns(new List<SessionFile> { sessionFile });
        var tab = CreateMockTab("Untitled-1");
        _tabFactory.Setup(factory => factory.CreateUntitled("same")).Returns(tab);

        await _sut.RestoreSessionAsync();
        await _sut.RestoreSessionAsync();

        Assert.Same(tab, Assert.Single(_sut.Tabs));
        _tabFactory.Verify(factory => factory.CreateUntitled("same"), Times.Once);
    }

    [Fact]
    public void SaveSession_CleanNamedTextCapturesBaseIdentity()
    {
        const string path = @"C:\notes.txt";
        var diskBytes = "disk"u8.ToArray();
        var tab = CreateMockTab("notes.txt", path);
        tab.SetContentBaseline("disk", isModified: false);
        _sut.Tabs.Add(tab);
        _fileSystemService.Setup(service => service.FileExists(path)).Returns(true);
        _fileSystemService.Setup(service => service.OpenRead(path))
            .Returns(() => new MemoryStream(diskBytes));

        _sut.SaveSession();

        _settingsService.VerifySet(service =>
            service.OpenFiles = It.Is<List<SessionFile>>(files =>
                files.Count == 1 &&
                !files[0].IsModified &&
                files[0].TextBaseLength == diskBytes.LongLength &&
                files[0].TextBaseSha256 ==
                Convert.ToHexString(SHA256.HashData(diskBytes))));
    }

    [Fact]
    public void SaveSession_CleanNamedTextChangedOnDiskPromotesSnapshotToModified()
    {
        const string path = @"C:\notes.txt";
        var tab = CreateMockTab("notes.txt", path);
        tab.SetContentBaseline("old content", isModified: false);
        _sut.Tabs.Add(tab);
        _fileSystemService.Setup(service => service.FileExists(path)).Returns(true);
        _fileSystemService.Setup(service => service.OpenRead(path))
            .Returns(() => new MemoryStream("new content"u8.ToArray()));

        _sut.SaveSession();

        _settingsService.VerifySet(service =>
            service.OpenFiles = It.Is<List<SessionFile>>(files =>
                files.Count == 1 &&
                files[0].Content == "old content" &&
                files[0].IsModified));
    }

    // --- Toggle commands ---

    [Fact]
    public void ToggleFolding_TogglesProperty()
    {
        Assert.True(_sut.IsFoldingEnabled);
        _sut.ToggleFoldingCommand.Execute(null);
        Assert.False(_sut.IsFoldingEnabled);
    }

    [Fact]
    public void ToggleMinimap_TogglesProperty()
    {
        Assert.False(_sut.IsMinimapVisible);
        _sut.ToggleMinimapCommand.Execute(null);
        Assert.True(_sut.IsMinimapVisible);
    }

    [Fact]
    public void ToggleAutoReload_TogglesProperty()
    {
        Assert.False(_sut.IsAutoReloadEnabled);
        _sut.ToggleAutoReloadCommand.Execute(null);
        Assert.True(_sut.IsAutoReloadEnabled);
    }

    [Fact]
    public void ToggleIndentGuides_TogglesProperty()
    {
        Assert.True(_sut.IsIndentGuidesEnabled);
        _sut.ToggleIndentGuidesCommand.Execute(null);
        Assert.False(_sut.IsIndentGuidesEnabled);
    }

    [Fact]
    public void ToggleCommandRunner_TogglesProperty()
    {
        Assert.False(_sut.IsCommandRunnerVisible);
        _sut.ToggleCommandRunnerCommand.Execute(null);
        Assert.True(_sut.IsCommandRunnerVisible);
    }

    // --- Event-raising commands ---

    [Fact]
    public void FindCommand_RaisesEvent()
    {
        bool raised = false;
        _sut.FindRequested += () => raised = true;
        _sut.FindCommand.Execute(null);
        Assert.True(raised);
    }

    [Fact]
    public void ReplaceCommand_RaisesEvent()
    {
        bool raised = false;
        _sut.ReplaceRequested += () => raised = true;
        _sut.ReplaceCommand.Execute(null);
        Assert.True(raised);
    }

    [Fact]
    public void DuplicateLineCommand_RaisesEvent()
    {
        bool raised = false;
        _sut.DuplicateLineRequested += () => raised = true;
        _sut.DuplicateLineCommand.Execute(null);
        Assert.True(raised);
    }

    [Fact]
    public void FormatDocumentCommand_RaisesEvent()
    {
        bool raised = false;
        _sut.FormatDocumentRequested += () => raised = true;
        _sut.FormatDocumentCommand.Execute(null);
        Assert.True(raised);
    }

    [Fact]
    public void FormatDocumentCommand_BinaryMode_DoesNotRaiseEvent()
    {
        _sut.SelectedTab = CreateMockTab(mode: FileOpenMode.Binary);
        bool raised = false;
        _sut.FormatDocumentRequested += () => raised = true;

        _sut.FormatDocumentCommand.Execute(null);

        Assert.False(raised);
        Assert.Contains("Hex mode", _sut.StatusText);
    }

    [Fact]
    public void CommandPaletteCommand_RaisesEvent()
    {
        bool raised = false;
        _sut.CommandPaletteRequested += () => raised = true;
        _sut.CommandPaletteCommand.Execute(null);
        Assert.True(raised);
    }

    // --- RefreshThemes ---

    [Fact]
    public void RefreshThemes_CallsServiceAndUpdates()
    {
        var newThemes = new List<ThemeDefinition> { new() { Name = "Test" } };
        _themeService.Setup(t => t.AvailableThemes).Returns(newThemes);

        _sut.RefreshThemesCommand.Execute(null);

        _themeService.Verify(t => t.RefreshCustomThemes(), Times.Once);
        Assert.Equal(newThemes, _sut.AvailableThemes);
    }

    // --- ChangeEncoding ---

    [Fact]
    public async Task ChangeEncoding_ReadsWithNewEncoding()
    {
        var tab = CreateMockTab("test.txt", @"C:\test.txt");
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);
        tab.FilePath = @"C:\test.txt";

        var bytes = System.Text.Encoding.ASCII.GetBytes("hello");
        _fileSystemService.Setup(f => f.ReadAllBytesAsync(@"C:\test.txt")).ReturnsAsync(bytes);

        await _sut.ChangeEncodingCommand.ExecuteAsync("us-ascii");

        Assert.Equal("hello", _sut.SelectedTab!.Content);
    }

    [Fact]
    public async Task ChangeEncoding_NoTab_DoesNothing()
    {
        await _sut.ChangeEncodingCommand.ExecuteAsync("utf-8");
        // Should not throw
    }
}
