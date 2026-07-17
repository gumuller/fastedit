using System.IO;
using System.Text;
using FastEdit.Infrastructure;
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
    public void AdoptRestoredTabs_StableIdentityDedupesAcrossNamedState()
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

        Assert.Same(liveNamed, Assert.Single(liveTabs));
        Assert.Same(stagedUntitled, Assert.Single(adoption.DiscardedDuplicateTabs));
        Assert.Empty(stagedUntitled.Content);
        Assert.Equal("live content", liveNamed.Content);
        Assert.Same(liveNamed, adoption.SelectedTab);
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
}
