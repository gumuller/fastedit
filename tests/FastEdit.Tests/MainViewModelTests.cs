using System.Collections.ObjectModel;
using System.IO;
using FastEdit.Helpers;
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
        _themeService.Setup(t => t.CurrentTheme).Returns((ThemeDefinition?)null);
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

    private EditorTabViewModel CreateMockTab(string fileName = "test.txt", string filePath = "", bool isModified = false)
    {
        var tab = new EditorTabViewModel(_fileService.Object, _fileSystemService.Object, _dialogService.Object)
        {
            FileName = fileName,
            FilePath = filePath,
            IsModified = isModified
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
    public void TextChecksumMd5Command_RaisesChecksumOperation()
    {
        TextToolOperation? operation = null;
        _sut.TextToolRequested += requestedOperation => operation = requestedOperation;

        _sut.TextChecksumMd5Command.Execute(null);

        Assert.Equal(TextToolOperation.ComputeMd5, operation);
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
    public void SaveSession_UntitledTab_WritesToTempFile()
    {
        var tab = CreateMockTab("Untitled-1");
        tab.Content = "hello world";
        _tabFactory.Setup(f => f.CreateUntitled(null)).Returns(tab);
        _sut.NewFileCommand.Execute(null);

        _sut.SaveSession();

        _fileSystemService.Verify(f => f.CreateDirectory(It.IsAny<string>()), Times.Once);
        _fileSystemService.Verify(f => f.WriteAllText(It.IsAny<string>(), "hello world"), Times.Once);
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
