using System.IO;
using System.Text;
using FastEdit.Infrastructure;
using FastEdit.Services;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;
using Moq;

namespace FastEdit.Tests;

public class DocumentSessionCoordinatorTests
{
    private readonly Mock<IFileService> _fileService = new();
    private readonly Mock<ISettingsService> _settingsService = new();
    private readonly Mock<IFileSystemService> _fileSystemService = new();
    private readonly Mock<IDialogService> _dialogService = new();
    private readonly Mock<IEditorTabFactory> _tabFactory = new();
    private readonly DocumentSessionCoordinator _sut;

    public DocumentSessionCoordinatorTests()
    {
        _sut = new DocumentSessionCoordinator(
            _settingsService.Object,
            _fileSystemService.Object,
            _tabFactory.Object);
    }

    [Fact]
    public void CreateNamedSession_ComposesUntitledContentAndActiveIndex()
    {
        var saved = CreateTab("saved.txt", @"C:\saved.txt");
        var untitled = CreateTab("Untitled-2");
        untitled.SetContentBaseline("draft", isModified: true);
        untitled.CursorOffset = 12;
        untitled.ScrollOffset = 4.5;

        var session = _sut.CreateNamedSession(new[] { saved, untitled }, untitled);

        Assert.Equal(1, session.ActiveTabIndex);
        Assert.Null(session.Files[0].Content);
        Assert.Equal("draft", session.Files[1].Content);
        Assert.True(session.Files[1].IsUntitled);
        Assert.Equal(12, session.Files[1].CursorOffset);
        Assert.Equal(4.5, session.Files[1].ScrollOffset);
        Assert.Equal(untitled.AutoSaveIdentity, session.Files[1].TabIdentity);
    }

    [Fact]
    public async Task StageNamedSession_LoadsAllTabsBeforeOwnershipTransfer()
    {
        var filePath = Path.GetTempFileName();
        try
        {
            var saved = CreateTab();
            var untitled = CreateTab();
            _fileSystemService.Setup(service => service.FileExists(filePath)).Returns(true);
            _fileService.Setup(service => service.ReadFileWithEncodingAsync(filePath))
                .ReturnsAsync(new FileReadResult("saved", Encoding.UTF8, false));
            _tabFactory.Setup(factory => factory.Create()).Returns(saved);
            _tabFactory.Setup(factory => factory.CreateUntitled("draft")).Returns(untitled);
            var session = new SessionData
            {
                ActiveTabIndex = 1,
                Files =
                {
                    new SessionFileEntry
                    {
                        FilePath = filePath,
                        TabIdentity = "saved-session-identity"
                    },
                    new SessionFileEntry
                    {
                        FilePath = "Untitled-2",
                        TabIdentity = "named-session-identity",
                        IsUntitled = true,
                        Content = "draft",
                        CursorOffset = 8
                    }
                }
            };

            using var staged = await _sut.StageNamedSessionAsync(session);

            Assert.Equal(new[] { saved, untitled }, staged.Tabs);
            Assert.Equal(1, staged.ActiveTabIndex);
            Assert.Equal("saved", saved.Content);
            Assert.Equal("saved-session-identity", saved.AutoSaveIdentity);
            Assert.Equal("Untitled-2", untitled.FileName);
            Assert.Equal("named-session-identity", untitled.AutoSaveIdentity);
            Assert.Equal(8, untitled.CursorOffset);
            Assert.Same(staged.Tabs, staged.AdoptTabs());
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task StageNamedSession_CaseVariantIdentityCollisionRekeysForNextAutoSave()
    {
        var first = CreateTab();
        first.SetContentBaseline("first", isModified: true);
        var second = CreateTab();
        second.SetContentBaseline("second", isModified: true);
        _tabFactory.Setup(factory => factory.CreateUntitled("first")).Returns(first);
        _tabFactory.Setup(factory => factory.CreateUntitled("second")).Returns(second);
        var generatedIdentities = new Queue<string>(
            new[] { "SHARED", "rekeyed" });
        var sut = new DocumentSessionCoordinator(
            _settingsService.Object,
            _fileSystemService.Object,
            _tabFactory.Object,
            generatedIdentities.Dequeue);
        var session = new SessionData
        {
            ActiveTabIndex = 1,
            Files =
            {
                new SessionFileEntry
                {
                    FilePath = "Untitled-1",
                    TabIdentity = "SHARED",
                    IsUntitled = true,
                    Content = "first"
                },
                new SessionFileEntry
                {
                    FilePath = "Untitled-2",
                    TabIdentity = "shared",
                    IsUntitled = true,
                    Content = "second"
                }
            }
        };

        using var staged = await sut.StageNamedSessionAsync(session);
        var autoSaveEntries = sut.CreateAutoSaveEntries(staged.Tabs);

        Assert.Equal(new[] { first, second }, staged.Tabs);
        Assert.Equal(1, staged.ActiveTabIndex);
        Assert.Equal("SHARED", first.AutoSaveIdentity);
        Assert.Equal("rekeyed", second.AutoSaveIdentity);
        Assert.Equal(
            2,
            autoSaveEntries
                .Select(entry => entry.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.Equal(
            new[] { "first", "second" },
            autoSaveEntries.Select(entry => entry.Content).ToArray());
    }

    [Fact]
    public async Task PrepareDiscard_ExcludesOnlyExplicitlyDiscardedUntitledTab()
    {
        var discarded = CreateTab("Untitled-discarded");
        discarded.SetContentBaseline("discard", isModified: true);
        var selected = CreateTab("selected.txt", @"C:\selected.txt");
        var tabs = new[] { discarded, selected };
        _sut.BeginShutdownPreparation();

        var preparation = await _sut.PrepareUnsavedChangesAsync(
            tabs,
            new[] { discarded },
            UnsavedChangesDecision.Discard,
            recordShutdownDiscards: true);
        _sut.PersistShutdownSession(tabs, selected);

        Assert.True(preparation.CanContinue);
        _settingsService.VerifySet(service => service.OpenFiles =
            It.Is<List<SessionFile>>(files =>
                files.Count == 1 &&
                files[0].FilePath == selected.FilePath &&
                files[0].TabIdentity == selected.AutoSaveIdentity));
        _settingsService.VerifySet(service => service.ActiveTabIndex = 0);
        _fileSystemService.Verify(
            service => service.WriteAllTextAtomic(It.IsAny<string>(), "discard"),
            Times.Never);
    }

    [Fact]
    public async Task PrepareDiscard_NamedTabPersistsCleanReferenceWithoutDirtySnapshot()
    {
        var discarded = CreateTab("saved.txt", @"C:\saved.txt");
        discarded.SetContentBaseline("unsaved edit", isModified: true);
        _settingsService.SetupProperty(
            service => service.OpenFiles,
            new List<SessionFile>());
        _settingsService.SetupProperty(service => service.ActiveTabIndex, 0);
        _sut.BeginShutdownPreparation();

        var preparation = await _sut.PrepareUnsavedChangesAsync(
            new[] { discarded },
            new[] { discarded },
            UnsavedChangesDecision.Discard,
            recordShutdownDiscards: true);
        _sut.PersistShutdownSession(new[] { discarded }, discarded);

        Assert.True(preparation.CanContinue);
        var persisted = Assert.Single(_settingsService.Object.OpenFiles);
        Assert.Equal(discarded.FilePath, persisted.FilePath);
        Assert.False(persisted.IsModified);
        Assert.Null(persisted.SnapshotGeneration);
        Assert.Null(persisted.SnapshotFile);
        _fileSystemService.Verify(service => service.WriteAllTextAtomic(
            It.IsAny<string>(),
            "unsaved edit"), Times.Never);
    }

    [Fact]
    public async Task ShutdownSession_RestoresViewStateBeforeDuplicatePlanning()
    {
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(filePath, "same");
            var original = CreateTab(Path.GetFileName(filePath), filePath);
            original.SetContentBaseline("same", isModified: false);
            original.CursorOffset = 17;
            original.ScrollOffset = 8.5;
            _settingsService.SetupProperty(
                service => service.OpenFiles,
                new List<SessionFile>());
            _settingsService.SetupProperty(service => service.ActiveTabIndex, 0);
            string? snapshotPath = null;
            _fileSystemService.Setup(service => service.WriteAllTextAtomic(
                    It.IsAny<string>(),
                    "same"))
                .Callback<string, string>((path, _) => snapshotPath = path);
            _sut.PersistShutdownSession(new[] { original }, original);
            var persisted = Assert.Single(_settingsService.Object.OpenFiles);
            Assert.Equal(filePath, persisted.FilePath);
            Assert.Equal(17, persisted.CursorOffset);
            Assert.Equal(8.5, persisted.ScrollOffset);
            var restored = CreateTab();
            _fileSystemService.Setup(service => service.FileExists(It.IsAny<string>()))
                .Returns<string>(path =>
                    path == filePath || path == snapshotPath);
            _fileSystemService.Setup(service => service.ReadAllTextAsync(
                    It.IsAny<string>()))
                .ReturnsAsync("same");
            _fileService.Setup(service => service.ReadFileWithEncodingAsync(filePath))
                .ReturnsAsync(new FileReadResult("same", Encoding.UTF8, false));
            _tabFactory.Setup(factory => factory.Create()).Returns(restored);

            using var restoredSession = await _sut.RestoreShutdownSessionAsync();

            var candidate = Assert.Single(restoredSession.Candidates);
            Assert.Equal(17, candidate.Tab.CursorOffset);
            Assert.Equal(8.5, candidate.Tab.ScrollOffset);
            var live = CreateTab(Path.GetFileName(filePath), filePath);
            live.RestoreAutoSaveIdentity(original.AutoSaveIdentity);
            live.SetContentBaseline("same", isModified: false);
            var liveTabs = new List<EditorTabViewModel> { live };

            var adoption = _sut.AdoptRestoredTabs(
                restoredSession,
                liveTabs,
                liveTabs.Add);

            Assert.Equal(2, liveTabs.Count);
            Assert.Contains(restored, liveTabs);
            Assert.Empty(adoption.DiscardedDuplicateTabs);
            Assert.NotEqual(
                live.AutoSaveIdentity,
                restored.AutoSaveIdentity,
                StringComparer.OrdinalIgnoreCase);
            Assert.Same(restored, adoption.SelectedTab);
            original.Dispose();
            foreach (var tab in liveTabs)
                tab.Dispose();
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ShutdownSession_DirtyNamedTextRestoresContentAndEncodingWithoutWritingSource()
    {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sourcePath = Path.Combine(workspace, "named.txt");
            var snapshotRoot = Path.Combine(workspace, "snapshots");
            Directory.CreateDirectory(workspace);
            await File.WriteAllTextAsync(sourcePath, "disk");
            var fileSystem = new FileSystemService();
            var store = new TestShutdownSessionStore();
            var original = new EditorTabViewModel(
                _fileService.Object,
                fileSystem,
                _dialogService.Object);
            _fileService.Setup(service => service.ReadFileWithEncodingAsync(sourcePath))
                .ReturnsAsync(new FileReadResult("disk", Encoding.Unicode, true));
            await original.LoadFileAsync(sourcePath);
            original.Content = "unsaved utf-16 edit";
            original.CursorOffset = 8;
            original.ScrollOffset = 3.5;
            var persistFactory = new Mock<IEditorTabFactory>();
            var persister = new DocumentSessionCoordinator(
                _settingsService.Object,
                fileSystem,
                persistFactory.Object,
                store,
                () => Guid.NewGuid().ToString("N"),
                null,
                () => "generation-one",
                snapshotRoot);

            persister.PersistShutdownSession(new[] { original }, original);

            Assert.Equal("disk", await File.ReadAllTextAsync(sourcePath));
            var persisted = Assert.Single(store.Current.Files);
            Assert.True(persisted.IsModified);
            Assert.Equal(Encoding.Unicode.CodePage, persisted.EncodingCodePage);
            Assert.True(persisted.HasBom);
            var restored = new EditorTabViewModel(
                _fileService.Object,
                fileSystem,
                _dialogService.Object);
            var restoreFactory = new Mock<IEditorTabFactory>();
            restoreFactory.Setup(factory => factory.Create()).Returns(restored);
            var restorer = new DocumentSessionCoordinator(
                _settingsService.Object,
                fileSystem,
                restoreFactory.Object,
                store,
                () => Guid.NewGuid().ToString("N"),
                null,
                () => "generation-two",
                snapshotRoot);

            using var restoredSession = await restorer.RestoreShutdownSessionAsync();
            var liveTabs = new List<EditorTabViewModel>();
            var adoption = restorer.AdoptRestoredTabs(
                restoredSession,
                liveTabs,
                liveTabs.Add);

            Assert.Same(restored, adoption.SelectedTab);
            Assert.Equal("unsaved utf-16 edit", restored.Content);
            Assert.True(restored.IsModified);
            Assert.Equal(Encoding.Unicode.CodePage, restored.FileEncoding.CodePage);
            Assert.True(restored.HasBom);
            Assert.Equal(8, restored.CursorOffset);
            Assert.Equal(3.5, restored.ScrollOffset);

            await restored.SaveCommand.ExecuteAsync(null);

            _fileService.Verify(service => service.WriteFileWithEncodingAsync(
                sourcePath,
                "unsaved utf-16 edit",
                It.Is<Encoding>(encoding =>
                    encoding.CodePage == Encoding.Unicode.CodePage),
                true));
            restorer.Dispose();
            original.Dispose();
            restored.Dispose();
            Directory.Delete(workspace, recursive: true);
        }

        [Fact]
        public async Task ShutdownSession_CleanNamedTextPreservesExplicitTextModeAndContent()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sourcePath = Path.Combine(workspace, "ambiguous.dat");
            var snapshotRoot = Path.Combine(workspace, "snapshots");
            Directory.CreateDirectory(workspace);
            await File.WriteAllBytesAsync(sourcePath, new byte[] { 0, 0, 0, 0 });
            var fileSystem = new FileSystemService();
            var store = new TestShutdownSessionStore();
            var original = new EditorTabViewModel(
                _fileService.Object,
                fileSystem,
                _dialogService.Object);
            original.RestoreTextSnapshot(
                sourcePath,
                "ambiguous.dat",
                "text interpretation",
                isModified: false,
                Encoding.UTF8.CodePage,
                hasBom: false);
            var factory = new Mock<IEditorTabFactory>();
            var persister = new DocumentSessionCoordinator(
                _settingsService.Object,
                fileSystem,
                factory.Object,
                store,
                () => Guid.NewGuid().ToString("N"),
                null,
                () => "text-generation",
                snapshotRoot);
            persister.PersistShutdownSession(new[] { original }, original);
            var restored = new EditorTabViewModel(
                _fileService.Object,
                fileSystem,
                _dialogService.Object);
            factory.Setup(item => item.Create()).Returns(restored);
            var restorer = new DocumentSessionCoordinator(
                _settingsService.Object,
                fileSystem,
                factory.Object,
                store,
                () => Guid.NewGuid().ToString("N"),
                null,
                () => "next-generation",
                snapshotRoot);

            using var restoredSession = await restorer.RestoreShutdownSessionAsync();
            var liveTabs = new List<EditorTabViewModel>();
            restorer.AdoptRestoredTabs(restoredSession, liveTabs, liveTabs.Add);

            Assert.Equal(FileOpenMode.Text, restored.Mode);
            Assert.Equal("text interpretation", restored.Content);
            Assert.False(restored.IsModified);
            restorer.Dispose();
            original.Dispose();
            restored.Dispose();
            Directory.Delete(workspace, recursive: true);
        }

        [Fact]
        public async Task ShutdownSession_DirtyBinarySurvivesMissingSourceAndExplicitSave()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sourcePath = Path.Combine(workspace, "data.bin");
            var snapshotRoot = Path.Combine(workspace, "snapshots");
            Directory.CreateDirectory(workspace);
            await File.WriteAllBytesAsync(sourcePath, new byte[] { 0, 1, 0, 3 });
            var fileSystem = new FileSystemService();
            var store = new TestShutdownSessionStore();
            var original = new EditorTabViewModel(
                _fileService.Object,
                fileSystem,
                _dialogService.Object);
            await original.LoadFileAsync(sourcePath);
            original.ByteBuffer!.SetByte(1, 42);
            original.HexOffset = 2;
            original.BytesPerRow = 24;
            var factory = new Mock<IEditorTabFactory>();
            var persister = new DocumentSessionCoordinator(
                _settingsService.Object,
                fileSystem,
                factory.Object,
                store,
                () => Guid.NewGuid().ToString("N"),
                null,
                () => "binary-generation",
                snapshotRoot);

            persister.PersistShutdownSession(new[] { original }, original);
            var originalSnapshot = Path.Combine(
                snapshotRoot,
                store.Current.Files[0].SnapshotGeneration!,
                store.Current.Files[0].SnapshotFile!);
            original.Dispose();
            File.Delete(sourcePath);

            var restored = new EditorTabViewModel(
                _fileService.Object,
                fileSystem,
                _dialogService.Object);
            factory.Setup(item => item.Create()).Returns(restored);
            var restorer = new DocumentSessionCoordinator(
                _settingsService.Object,
                fileSystem,
                factory.Object,
                store,
                () => Guid.NewGuid().ToString("N"),
                null,
                () => "next-generation",
                snapshotRoot);
            using var restoredSession = await restorer.RestoreShutdownSessionAsync();
            var liveTabs = new List<EditorTabViewModel>();
            restorer.AdoptRestoredTabs(restoredSession, liveTabs, liveTabs.Add);

            Assert.Equal(FileOpenMode.Binary, restored.Mode);
            Assert.True(restored.IsModified);
            Assert.Equal(2, restored.HexOffset);
            Assert.Equal(24, restored.BytesPerRow);
            Assert.Equal(
                new byte[] { 0, 42, 0, 3 },
                restored.ByteBuffer!.GetBytes(0, 4).ToArray());

            restorer.PersistShutdownSession(liveTabs, restored);

            Assert.False(File.Exists(originalSnapshot));
            await restored.SaveCommand.ExecuteAsync(null);

            Assert.Equal(new byte[] { 0, 42, 0, 3 }, await File.ReadAllBytesAsync(sourcePath));
            Assert.False(restored.IsModified);
            restorer.Dispose();
            restored.Dispose();
            Directory.Delete(workspace, recursive: true);
        }

        [Fact]
        public async Task ShutdownSession_CleanUntitledRemainsCleanAndFailedEntryCarriesForward()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var snapshotRoot = Path.Combine(workspace, "snapshots");
            var validDirectory = Path.Combine(snapshotRoot, "valid-generation");
            Directory.CreateDirectory(validDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(validDirectory, "tab-valid.txt"),
                "clean draft");
            var unresolved = new SessionFile
            {
                FilePath = @"C:\missing.txt",
                FileName = "missing.txt",
                TabIdentity = "missing",
                IsModified = true,
                SnapshotGeneration = "missing-generation",
                SnapshotFile = "tab-missing.txt"
            };
            var valid = new SessionFile
            {
                FilePath = "Untitled-1",
                FileName = "Untitled-1",
                TabIdentity = "valid",
                IsUntitled = true,
                IsModified = false,
                IsActive = true,
                SnapshotGeneration = "valid-generation",
                SnapshotFile = "tab-valid.txt"
            };
            var store = new TestShutdownSessionStore(
                new ShutdownSessionState(new[] { unresolved, valid }, 1));
            var fileSystem = new FileSystemService();
            var restored = new EditorTabViewModel(
                _fileService.Object,
                fileSystem,
                _dialogService.Object);
            var factory = new Mock<IEditorTabFactory>();
            factory.Setup(item => item.Create()).Returns(restored);
            var generations = new Queue<string>(new[] { "next-generation" });
            var sut = new DocumentSessionCoordinator(
                _settingsService.Object,
                fileSystem,
                factory.Object,
                store,
                () => Guid.NewGuid().ToString("N"),
                null,
                generations.Dequeue,
                snapshotRoot);

            using var restoredSession = await sut.RestoreShutdownSessionAsync();
            var liveTabs = new List<EditorTabViewModel>();
            var adoption = sut.AdoptRestoredTabs(
                restoredSession,
                liveTabs,
                liveTabs.Add);

            Assert.Same(restored, adoption.SelectedTab);
            Assert.Equal("clean draft", restored.Content);
            Assert.False(restored.IsModified);

            sut.PersistShutdownSession(liveTabs, restored);

            Assert.Equal(2, store.Current.Files.Count);
            Assert.Contains(
                store.Current.Files,
                file => file.TabIdentity == "missing" &&
                    file.SnapshotGeneration == "missing-generation");
            var republished = Assert.Single(
                store.Current.Files,
                file => file.TabIdentity == "valid");
            Assert.False(republished.IsModified);
            Assert.True(republished.IsActive);
            restored.Dispose();
            Directory.Delete(workspace, recursive: true);
    }

    [Fact]
    public void ShutdownSession_PublicationFailurePreservesPreviousAndRemovesPrivateGeneration()
    {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var snapshotRoot = Path.Combine(workspace, "snapshots");
            var oldDirectory = Path.Combine(snapshotRoot, "old-generation");
            Directory.CreateDirectory(oldDirectory);
            var oldSnapshot = Path.Combine(oldDirectory, "tab-old.txt");
            File.WriteAllText(oldSnapshot, "last good");
            var previous = new SessionFile
            {
                FilePath = "Untitled-old",
                FileName = "Untitled-old",
                TabIdentity = "old",
                IsUntitled = true,
                IsModified = true,
                SnapshotGeneration = "old-generation",
                SnapshotFile = "tab-old.txt",
                SnapshotGenerationFiles = new List<string> { "tab-old.txt" }
            };
            var store = new TestShutdownSessionStore(
                new ShutdownSessionState(new[] { previous }, 0))
            {
                PublicationFailure = new IOException("settings unavailable")
            };
            var fileSystem = new FileSystemService();
            var tab = new EditorTabViewModel(
                _fileService.Object,
                fileSystem,
                _dialogService.Object)
            {
                FileName = "Untitled-new"
            };
            tab.SetContentBaseline("new draft", isModified: true);
            var factory = new Mock<IEditorTabFactory>();
            var sut = new DocumentSessionCoordinator(
                _settingsService.Object,
                fileSystem,
                factory.Object,
                store,
                () => Guid.NewGuid().ToString("N"),
                null,
                () => "new-generation",
                snapshotRoot);

            Assert.Throws<IOException>(() =>
                sut.PersistShutdownSession(new[] { tab }, tab));

            Assert.True(File.Exists(oldSnapshot));
            Assert.False(Directory.Exists(
                Path.Combine(snapshotRoot, "new-generation")));
            Assert.Equal("old-generation", Assert.Single(store.Current.Files).SnapshotGeneration);
            tab.Dispose();
            Directory.Delete(workspace, recursive: true);
    }

    [Fact]
    public async Task ShutdownSession_ConcurrentPublishersKeepFinalReferencedGeneration()
    {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var appData = Path.Combine(workspace, "settings");
            var snapshotRoot = Path.Combine(workspace, "snapshots");
            Directory.CreateDirectory(appData);
            var fileSystem = new FileSystemService();
            var firstStore = new SettingsService(fileSystem, appData);
            var secondStore = new SettingsService(fileSystem, appData);
            var firstTab = new EditorTabViewModel(
                _fileService.Object,
                fileSystem,
                _dialogService.Object)
            {
                FileName = "Untitled-first"
            };
            firstTab.SetContentBaseline("first", isModified: true);
            var secondTab = new EditorTabViewModel(
                _fileService.Object,
                fileSystem,
                _dialogService.Object)
            {
                FileName = "Untitled-second"
            };
            secondTab.SetContentBaseline("second", isModified: true);
            var factory = new Mock<IEditorTabFactory>();
            var firstCoordinator = new DocumentSessionCoordinator(
                firstStore,
                fileSystem,
                factory.Object,
                firstStore,
                () => Guid.NewGuid().ToString("N"),
                null,
                () => "first-generation",
                snapshotRoot);
            var secondCoordinator = new DocumentSessionCoordinator(
                secondStore,
                fileSystem,
                factory.Object,
                secondStore,
                () => Guid.NewGuid().ToString("N"),
                null,
                () => "second-generation",
                snapshotRoot);

            await Task.WhenAll(
                Task.Run(() =>
                    firstCoordinator.PersistShutdownSession(
                        new[] { firstTab },
                        firstTab)),
                Task.Run(() =>
                    secondCoordinator.PersistShutdownSession(
                        new[] { secondTab },
                        secondTab)));

            var finalSession = new SettingsService(
                fileSystem,
                appData).ReadShutdownSession();
            Assert.Equal(2, finalSession.Files.Count);
            foreach (var finalFile in finalSession.Files)
            {
                var finalPath = Path.Combine(
                    snapshotRoot,
                    finalFile.SnapshotGeneration!,
                    finalFile.SnapshotFile!);
                Assert.True(File.Exists(finalPath));
                Assert.Equal(
                    finalFile.FileName == "Untitled-first" ? "first" : "second",
                    File.ReadAllText(finalPath));
            }
            firstTab.Dispose();
            secondTab.Dispose();
            Directory.Delete(workspace, recursive: true);
    }

    [Fact]
    public async Task ShutdownSession_GenerationLeaseDefersRetirementUntilAllRestorersRelease()
    {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var appData = Path.Combine(workspace, "settings");
            var snapshotRoot = Path.Combine(workspace, "snapshots");
            Directory.CreateDirectory(appData);
            var fileSystem = new FileSystemService();
            var initialStore = new SettingsService(fileSystem, appData);
            var original = new EditorTabViewModel(
                _fileService.Object,
                fileSystem,
                _dialogService.Object)
            {
                FileName = "Untitled-1"
            };
            original.SetContentBaseline("shared recovery", isModified: true);
            var emptyFactory = new Mock<IEditorTabFactory>();
            var initialCoordinator = new DocumentSessionCoordinator(
                initialStore,
                fileSystem,
                emptyFactory.Object,
                initialStore,
                () => Guid.NewGuid().ToString("N"),
                null,
                () => "shared-generation",
                snapshotRoot,
                () => "original-owner");
            initialCoordinator.PersistShutdownSession(new[] { original }, original);
            var sharedSnapshot = Path.Combine(
                snapshotRoot,
                "shared-generation",
                $"tab-{original.AutoSaveIdentity}.txt");

            var firstStore = new SettingsService(fileSystem, appData);
            var firstTab = new EditorTabViewModel(
                _fileService.Object,
                fileSystem,
                _dialogService.Object);
            var firstFactory = new Mock<IEditorTabFactory>();
            firstFactory.Setup(factory => factory.Create()).Returns(firstTab);
            var firstCoordinator = new DocumentSessionCoordinator(
                firstStore,
                fileSystem,
                firstFactory.Object,
                firstStore,
                () => Guid.NewGuid().ToString("N"),
                null,
                () => "first-generation",
                snapshotRoot,
                () => "first-owner");
            var secondStore = new SettingsService(fileSystem, appData);
            var secondTab = new EditorTabViewModel(
                _fileService.Object,
                fileSystem,
                _dialogService.Object);
            var secondFactory = new Mock<IEditorTabFactory>();
            secondFactory.Setup(factory => factory.Create()).Returns(secondTab);
            var secondCoordinator = new DocumentSessionCoordinator(
                secondStore,
                fileSystem,
                secondFactory.Object,
                secondStore,
                () => Guid.NewGuid().ToString("N"),
                null,
                () => "second-generation",
                snapshotRoot,
                () => "second-owner");

            using var firstRestore = await firstCoordinator.RestoreShutdownSessionAsync();
            using var secondRestore = await secondCoordinator.RestoreShutdownSessionAsync();
            var firstLive = new List<EditorTabViewModel>();
            var secondLive = new List<EditorTabViewModel>();
            firstCoordinator.AdoptRestoredTabs(firstRestore, firstLive, firstLive.Add);
            secondCoordinator.AdoptRestoredTabs(secondRestore, secondLive, secondLive.Add);

            firstCoordinator.PersistShutdownSession(firstLive, firstTab);

            Assert.True(File.Exists(sharedSnapshot));

            secondCoordinator.PersistShutdownSession(secondLive, secondTab);

            Assert.False(File.Exists(sharedSnapshot));
            Assert.Equal(
                2,
                new SettingsService(fileSystem, appData)
                    .ReadShutdownSession()
                    .Files
                    .Count);
            original.Dispose();
            firstTab.Dispose();
            secondTab.Dispose();
            Directory.Delete(workspace, recursive: true);
    }

    [Fact]
    public async Task PrepareSave_EditDuringWriteReturnsRaceFailure()
    {
        var tab = CreateTab("saved.txt", @"C:\saved.txt");
        tab.Content = "snapshot";
        var writeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var writeCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _fileService.Setup(service => service.WriteFileWithEncodingAsync(
                tab.FilePath,
                "snapshot",
                It.IsAny<Encoding>(),
                It.IsAny<bool>()))
            .Returns(() =>
            {
                writeStarted.SetResult();
                return writeCompletion.Task;
            });

        var preparationTask = _sut.PrepareUnsavedChangesAsync(
            new[] { tab },
            new[] { tab },
            UnsavedChangesDecision.Save,
            recordShutdownDiscards: false);
        await writeStarted.Task;
        tab.Content = "newer edit";
        writeCompletion.SetResult();
        var preparation = await preparationTask;

        Assert.False(preparation.CanContinue);
        Assert.True(tab.IsModified);
        Assert.Null(preparation.FailureMessage);
    }

    [Fact]
    public void CreateAutoSaveEntries_UsesStableCollisionFreeTabIdentity()
    {
        var first = CreateTab("File.txt", @"C:\CaseSensitive\File.txt");
        var second = CreateTab("file.txt", @"C:\CaseSensitive\file.txt");
        first.SetContentBaseline("first", isModified: true);
        second.SetContentBaseline("second", isModified: true);

        var before = _sut.CreateAutoSaveEntries(new[] { first, second });
        var after = _sut.CreateAutoSaveEntries(new[] { second, first });

        Assert.Equal(2, before.Select(entry => entry.Id).Distinct().Count());
        Assert.Equal(
            before.ToDictionary(entry => entry.Content, entry => entry.Id),
            after.ToDictionary(entry => entry.Content, entry => entry.Id));
        Assert.Equal(first.AutoSaveIdentity, before[0].TabIdentity);
    }

    [Fact]
    public void RecoverTabs_PartialCreationReturnsRecoveredTabsAndSourceIds()
    {
        var recovered = CreateTab();
        _tabFactory.Setup(factory => factory.CreateUntitled("one")).Returns(recovered);
        _tabFactory.Setup(factory => factory.CreateUntitled("two"))
            .Throws(new InvalidOperationException("restore failed"));

        var result = _sut.RecoverTabs(
            new[]
            {
                new AutoSaveEntry(
                    "one-id",
                    "one.txt",
                    null,
                    "one",
                    true,
                    TabIdentity: "stable-one"),
                new AutoSaveEntry("two-id", "two.txt", null, "two", true)
            });

        Assert.False(result.Success);
        Assert.Equal(new[] { "one-id" }, result.RecoveredEntryIds);
        Assert.Same(recovered, Assert.Single(result.RecoveredTabs!));
        Assert.True(recovered.IsModified);
        Assert.Equal("stable-one", recovered.AutoSaveIdentity);
    }

    [Fact]
    public void PlanRecoveryBatch_DivergentSavedGenerationsRetainFirstAndRekeyAdditional()
    {
        var generatedIdentities = new Queue<string>(new[] { "rekeyed" });
        var sut = new DocumentSessionCoordinator(
            _settingsService.Object,
            _fileSystemService.Object,
            _tabFactory.Object,
            generatedIdentities.Dequeue);
        var entries = new[]
        {
            new AutoSaveEntry(
                "generation-a:tab-shared",
                "saved.txt",
                @"C:\saved.txt",
                "first edit",
                false,
                TabIdentity: "shared"),
            new AutoSaveEntry(
                "generation-b:tab-shared",
                "saved.txt",
                @"C:\saved.txt",
                "second edit",
                false,
                TabIdentity: "shared")
        };

        var plan = sut.PlanRecoveryBatch(
            entries,
            Array.Empty<EditorTabViewModel>());

        Assert.Equal(2, plan.Candidates.Count);
        Assert.Equal("shared", plan.Candidates[0].AssignedIdentity);
        Assert.Equal("rekeyed", plan.Candidates[1].AssignedIdentity);
        Assert.Empty(plan.DuplicateEntryIds);
    }

    [Fact]
    public void PlanRecoveryBatch_LiveIdentityCollisionRekeysDivergentCandidate()
    {
        var live = CreateTab("Untitled-1");
        live.RestoreAutoSaveIdentity("shared");
        live.SetContentBaseline("live", isModified: true);
        var sut = new DocumentSessionCoordinator(
            _settingsService.Object,
            _fileSystemService.Object,
            _tabFactory.Object,
            () => "rekeyed");
        var entry = new AutoSaveEntry(
            "source",
            "Untitled-1",
            null,
            "divergent",
            true,
            TabIdentity: "shared");

        var plan = sut.PlanRecoveryBatch(new[] { entry }, new[] { live });

        Assert.Equal("rekeyed", Assert.Single(plan.Candidates).AssignedIdentity);
        Assert.Empty(plan.DuplicateEntryIds);
    }

    [Fact]
    public void PlanRecoveryBatch_ExactLiveDuplicateSkipsWithoutRekeying()
    {
        var generatorCalls = 0;
        var live = CreateTab("Untitled-1");
        live.RestoreAutoSaveIdentity("shared");
        live.SetContentBaseline("same", isModified: true);
        var sut = new DocumentSessionCoordinator(
            _settingsService.Object,
            _fileSystemService.Object,
            _tabFactory.Object,
            () =>
            {
                generatorCalls++;
                return "unused";
            });
        var entry = new AutoSaveEntry(
            "source",
            "Untitled-1",
            null,
            "same",
            true,
            TabIdentity: "shared");

        var result = sut.RecoverTabs(new[] { entry }, new[] { live });

        Assert.True(result.Success);
        Assert.Empty(result.RecoveredTabs!);
        Assert.Equal(new[] { "source" }, result.RecoveredEntryIds);
        Assert.Equal(0, generatorCalls);
        _tabFactory.Verify(
            factory => factory.CreateUntitled(It.IsAny<string?>()),
            Times.Never);
        Assert.Equal("shared", live.AutoSaveIdentity);
        Assert.Equal("same", live.Content);
    }

    [Fact]
    public void PlanRecoveryBatch_DifferentCaretStateIsDivergent()
    {
        var live = CreateTab("Untitled-1");
        live.RestoreAutoSaveIdentity("shared");
        live.SetContentBaseline("same", isModified: true);
        live.CursorOffset = 1;
        var sut = new DocumentSessionCoordinator(
            _settingsService.Object,
            _fileSystemService.Object,
            _tabFactory.Object,
            () => "rekeyed");
        var entry = new AutoSaveEntry(
            "source",
            "Untitled-1",
            null,
            "same",
            true,
            CursorOffset: 2,
            TabIdentity: "shared");

        var plan = sut.PlanRecoveryBatch(new[] { entry }, new[] { live });

        Assert.Equal("rekeyed", Assert.Single(plan.Candidates).AssignedIdentity);
        Assert.Empty(plan.DuplicateEntryIds);
    }

    [Fact]
    public void PlanRecoveryBatch_RekeyedOwnerStillDedupesExactPersistedIdentity()
    {
        var live = CreateTab("Untitled-1");
        live.RestoreAutoSaveIdentity("shared");
        live.SetContentBaseline("live", isModified: true);
        var generatorCalls = 0;
        var sut = new DocumentSessionCoordinator(
            _settingsService.Object,
            _fileSystemService.Object,
            _tabFactory.Object,
            () =>
            {
                generatorCalls++;
                return "rekeyed";
            });
        var entries = new[]
        {
            new AutoSaveEntry(
                "first",
                "Untitled-2",
                null,
                "same",
                true,
                TabIdentity: "shared"),
            new AutoSaveEntry(
                "duplicate",
                "Untitled-2",
                null,
                "same",
                true,
                TabIdentity: "shared")
        };

        var plan = sut.PlanRecoveryBatch(entries, new[] { live });

        Assert.Equal("rekeyed", Assert.Single(plan.Candidates).AssignedIdentity);
        Assert.Equal(new[] { "duplicate" }, plan.DuplicateEntryIds);
        Assert.Equal(1, generatorCalls);
    }

    [Fact]
    public void PlanRecoveryBatch_UnsafeIdentitySkipsReservedGeneratorCollision()
    {
        var generated = new Queue<string>(new[] { "persisted", "generated" });
        var sut = new DocumentSessionCoordinator(
            _settingsService.Object,
            _fileSystemService.Object,
            _tabFactory.Object,
            generated.Dequeue);
        var entries = new[]
        {
            new AutoSaveEntry(
                "legacy",
                "Untitled-1",
                null,
                "legacy",
                true,
                TabIdentity: @"..\legacy"),
            new AutoSaveEntry(
                "persisted",
                "Untitled-2",
                null,
                "persisted",
                true,
                TabIdentity: "persisted")
        };

        var plan = sut.PlanRecoveryBatch(
            entries,
            Array.Empty<EditorTabViewModel>());

        Assert.Equal(
            new[] { "generated", "persisted" },
            plan.Candidates.Select(candidate => candidate.AssignedIdentity));
        Assert.Empty(plan.DuplicateEntryIds);
    }

    [Fact]
    public void PlanRecoveryBatch_AlwaysCollidingGeneratorUsesFallback()
    {
        var sut = new DocumentSessionCoordinator(
            _settingsService.Object,
            _fileSystemService.Object,
            _tabFactory.Object,
            () => "SHARED",
            () => "fallback");
        var entries = new[]
        {
            new AutoSaveEntry(
                "first",
                "Untitled-1",
                null,
                "first",
                true,
                TabIdentity: "shared"),
            new AutoSaveEntry(
                "second",
                "Untitled-2",
                null,
                "second",
                true,
                TabIdentity: "SHARED")
        };

        var plan = sut.PlanRecoveryBatch(
            entries,
            Array.Empty<EditorTabViewModel>());

        Assert.Equal(
            new[] { "shared", "fallback" },
            plan.Candidates.Select(candidate => candidate.AssignedIdentity));
    }

    [Fact]
    public async Task StageNamedSession_FallbackCollisionDisposesStagedOwnership()
    {
        var first = CreateTab();
        first.SetContentBaseline("first", isModified: true);
        var second = CreateTab();
        second.SetContentBaseline("second", isModified: true);
        _tabFactory.Setup(factory => factory.CreateUntitled("first")).Returns(first);
        _tabFactory.Setup(factory => factory.CreateUntitled("second")).Returns(second);
        var sut = new DocumentSessionCoordinator(
            _settingsService.Object,
            _fileSystemService.Object,
            _tabFactory.Object,
            () => "shared",
            () => "SHARED");
        var session = new SessionData
        {
            Files =
            {
                new SessionFileEntry
                {
                    FilePath = "Untitled-1",
                    TabIdentity = "shared",
                    IsUntitled = true,
                    Content = "first"
                },
                new SessionFileEntry
                {
                    FilePath = "Untitled-2",
                    TabIdentity = "SHARED",
                    IsUntitled = true,
                    Content = "second"
                }
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.StageNamedSessionAsync(session));

        Assert.Empty(first.Content);
        Assert.Empty(second.Content);
    }

    [Fact]
    public void AdoptRestoredTabs_FallbackCollisionDisposesTransferredOwnership()
    {
        var first = CreateTab("Untitled-1");
        first.SetContentBaseline("first", isModified: true);
        var second = CreateTab("Untitled-2");
        second.SetContentBaseline("second", isModified: true);
        var sut = new DocumentSessionCoordinator(
            _settingsService.Object,
            _fileSystemService.Object,
            _tabFactory.Object,
            () => "shared",
            () => "SHARED");
        using var restoredSession = new RestoredDocumentSession(
            new[]
            {
                new RestoredTabCandidate(first, 0, "shared"),
                new RestoredTabCandidate(second, 1, "SHARED")
            },
            activeTabIndex: 0);

        Assert.Throws<InvalidOperationException>(() =>
            sut.AdoptRestoredTabs(
                restoredSession,
                Array.Empty<EditorTabViewModel>(),
                _ => throw new InvalidOperationException()));

        Assert.Empty(first.Content);
        Assert.Empty(second.Content);
    }

    [Fact]
    public void PlanRecoveryBatch_ExactSavedDuplicatePromotesAvailableValidIdentity()
    {
        var sut = new DocumentSessionCoordinator(
            _settingsService.Object,
            _fileSystemService.Object,
            _tabFactory.Object,
            () => "generated");
        var entries = new[]
        {
            new AutoSaveEntry(
                "legacy",
                "saved.txt",
                @"C:\saved.txt",
                "same",
                false,
                TabIdentity: " "),
            new AutoSaveEntry(
                "persisted",
                "saved.txt",
                @"C:\saved.txt",
                "same",
                false,
                TabIdentity: "valid")
        };

        var plan = sut.PlanRecoveryBatch(
            entries,
            Array.Empty<EditorTabViewModel>());

        Assert.Equal("valid", Assert.Single(plan.Candidates).AssignedIdentity);
        Assert.Equal(new[] { "persisted" }, plan.DuplicateEntryIds);
    }

    [Fact]
    public void PlanRecoveryBatch_ExactSavedDuplicateRetainsFirstValidIdentity()
    {
        var entries = new[]
        {
            new AutoSaveEntry(
                "first",
                "saved.txt",
                @"C:\saved.txt",
                "same",
                false,
                TabIdentity: "first-identity"),
            new AutoSaveEntry(
                "duplicate",
                "saved.txt",
                @"C:\saved.txt",
                "same",
                false,
                TabIdentity: "second-identity")
        };

        var plan = _sut.PlanRecoveryBatch(
            entries,
            Array.Empty<EditorTabViewModel>());

        Assert.Equal(
            "first-identity",
            Assert.Single(plan.Candidates).AssignedIdentity);
        Assert.Equal(new[] { "duplicate" }, plan.DuplicateEntryIds);
    }

    [Fact]
    public void AdoptRestoredTabs_CaseVariantIsNotDiscarded()
    {
        var live = CreateTab("File.txt", @"C:\CaseSensitive\File.txt");
        var candidate = CreateTab("file.txt", @"C:\CaseSensitive\file.txt");
        candidate.SetContentBaseline("candidate", isModified: false);
        var liveTabs = new List<EditorTabViewModel> { live };
        using var restoredSession = new RestoredDocumentSession(
            new[] { new RestoredTabCandidate(candidate, 0) },
            activeTabIndex: 0);

        var adoption = _sut.AdoptRestoredTabs(
            restoredSession,
            liveTabs,
            liveTabs.Add);

        Assert.Equal(2, liveTabs.Count);
        Assert.Empty(adoption.DiscardedDuplicateTabs);
        Assert.Same(candidate, adoption.SelectedTab);
        Assert.Equal("candidate", candidate.Content);
    }

    [Fact]
    public void AdoptRestoredTabs_MissingActiveCandidateFallsBackToClampedLiveIndex()
    {
        var existing = CreateTab("existing.txt", @"C:\existing.txt");
        var candidate = CreateTab("restored.txt", @"C:\restored.txt");
        var liveTabs = new List<EditorTabViewModel> { existing };
        using var restoredSession = new RestoredDocumentSession(
            new[] { new RestoredTabCandidate(candidate, 0) },
            activeTabIndex: 5);

        var adoption = _sut.AdoptRestoredTabs(
            restoredSession,
            liveTabs,
            liveTabs.Add);

        Assert.Same(candidate, adoption.SelectedTab);
    }

    [Fact]
    public void AdoptRestoredTabs_DistinctUntitledCollisionsAllocateDeterministicNames()
    {
        var live = CreateTab("Untitled-1");
        live.RestoreAutoSaveIdentity("live");
        var first = CreateTab("Untitled-1");
        first.RestoreAutoSaveIdentity("first");
        first.SetContentBaseline("first content", isModified: true);
        var second = CreateTab("Untitled-1");
        second.RestoreAutoSaveIdentity("second");
        second.SetContentBaseline("second content", isModified: true);
        var liveTabs = new List<EditorTabViewModel> { live };
        using var restoredSession = new RestoredDocumentSession(
            new[]
            {
                new RestoredTabCandidate(first, 0, "first"),
                new RestoredTabCandidate(second, 1, "second")
            },
            activeTabIndex: 1);

        var adoption = _sut.AdoptRestoredTabs(
            restoredSession,
            liveTabs,
            liveTabs.Add);

        Assert.Equal("Untitled-2", first.FileName);
        Assert.Equal("Untitled-3", second.FileName);
        Assert.Equal("first content", first.Content);
        Assert.Equal("second content", second.Content);
        Assert.Empty(adoption.DiscardedDuplicateTabs);
        Assert.Same(second, adoption.SelectedTab);
    }

    [Fact]
    public void AdoptRestoredTabs_DivergentNamedStateWithSameIdentityIsRekeyed()
    {
        var liveNamed = CreateTab("saved.txt", @"C:\saved.txt");
        liveNamed.RestoreAutoSaveIdentity("shared");
        liveNamed.SetContentBaseline("live content", isModified: true);
        var stagedUntitled = CreateTab("Untitled-1");
        stagedUntitled.RestoreAutoSaveIdentity("shared");
        stagedUntitled.SetContentBaseline("staged content", isModified: true);
        var liveTabs = new List<EditorTabViewModel> { liveNamed };
        using var restoredSession = new RestoredDocumentSession(
            new[] { new RestoredTabCandidate(stagedUntitled, 0, "shared") },
            activeTabIndex: 0);

        var adoption = _sut.AdoptRestoredTabs(
            restoredSession,
            liveTabs,
            liveTabs.Add);

        Assert.Equal(2, liveTabs.Count);
        Assert.Contains(stagedUntitled, liveTabs);
        Assert.Empty(adoption.DiscardedDuplicateTabs);
        Assert.NotEqual(liveNamed.AutoSaveIdentity, stagedUntitled.AutoSaveIdentity);
        Assert.Equal("staged content", stagedUntitled.Content);
        Assert.Equal("live content", liveNamed.Content);
        Assert.Same(stagedUntitled, adoption.SelectedTab);
    }

    [Fact]
    public void AdoptRestoredTabs_ExactStableIdentityDuplicateKeepsSingleOwner()
    {
        var live = CreateTab("Untitled-1");
        live.RestoreAutoSaveIdentity("shared");
        live.SetContentBaseline("same content", isModified: true);
        var staged = CreateTab("Untitled-1");
        staged.SetContentBaseline("same content", isModified: true);
        var liveTabs = new List<EditorTabViewModel> { live };
        using var restoredSession = new RestoredDocumentSession(
            new[] { new RestoredTabCandidate(staged, 0, "shared") },
            activeTabIndex: 0);

        var adoption = _sut.AdoptRestoredTabs(
            restoredSession,
            liveTabs,
            liveTabs.Add);

        Assert.Same(live, Assert.Single(liveTabs));
        Assert.Same(staged, Assert.Single(adoption.DiscardedDuplicateTabs));
        Assert.Empty(staged.Content);
        Assert.Equal("same content", live.Content);
        Assert.Same(live, adoption.SelectedTab);
    }

    [Fact]
    public void AdoptRestoredTabs_LegacyUntitledSameNameIsRetainedWithoutProof()
    {
        var live = CreateTab("Untitled-1");
        live.SetContentBaseline("live content", isModified: true);
        var legacyCandidate = CreateTab("Untitled-1");
        legacyCandidate.SetContentBaseline("legacy content", isModified: true);
        var liveTabs = new List<EditorTabViewModel> { live };
        using var restoredSession = new RestoredDocumentSession(
            new[] { new RestoredTabCandidate(legacyCandidate, 0) },
            activeTabIndex: 0);

        var adoption = _sut.AdoptRestoredTabs(
            restoredSession,
            liveTabs,
            liveTabs.Add);

        Assert.Equal(2, liveTabs.Count);
        Assert.Equal("Untitled-1", live.FileName);
        Assert.Equal("Untitled-2", legacyCandidate.FileName);
        Assert.Equal("live content", live.Content);
        Assert.Equal("legacy content", legacyCandidate.Content);
        Assert.Empty(adoption.DiscardedDuplicateTabs);
        Assert.Same(legacyCandidate, adoption.SelectedTab);
    }

    private EditorTabViewModel CreateTab(
        string fileName = "test.txt",
        string filePath = "") =>
        new(_fileService.Object, _fileSystemService.Object, _dialogService.Object)
        {
            FileName = fileName,
            FilePath = filePath
        };

    private sealed class TestShutdownSessionStore : IShutdownSessionStore
    {
        public TestShutdownSessionStore(ShutdownSessionState? initial = null)
        {
            Current = initial ?? new ShutdownSessionState(
                Array.Empty<SessionFile>(),
                0);
        }

        public ShutdownSessionState Current { get; private set; }

        public Exception? PublicationFailure { get; init; }

        public ShutdownSessionState ReadShutdownSession(
            Action<ShutdownSessionState>? whileLocked = null)
        {
            whileLocked?.Invoke(Current);
            return Current;
        }

        public ShutdownSessionPublication PublishShutdownSession(
            ShutdownSessionState session,
            Action<ShutdownSessionPublication>? whileLocked = null)
        {
            if (PublicationFailure != null)
                throw PublicationFailure;
            var previous = Current;
            Current = session;
            var publication = new ShutdownSessionPublication(previous, session);
            whileLocked?.Invoke(publication);
            return publication;
        }
    }
}
