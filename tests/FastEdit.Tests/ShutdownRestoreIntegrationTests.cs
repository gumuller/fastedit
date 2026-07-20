using System.IO;
using System.Text;
using FastEdit.Infrastructure;
using FastEdit.Services;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;
using Moq;

namespace FastEdit.Tests;

public sealed class ShutdownRestoreIntegrationTests
{
    [Fact]
    public async Task DirtyUntitledWholeAppShutdown_PersistsWithoutPromptAndNextStartupAdopts()
    {
        var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var settingsRoot = Path.Combine(workspace, "settings");
        var snapshotRoot = Path.Combine(workspace, "shutdown-snapshots");
        Directory.CreateDirectory(workspace);
        var fileSystem = new FileSystemService();
        var dialog = new Mock<IDialogService>(MockBehavior.Strict);
        var fileService = Mock.Of<IFileService>();
        var tabFactory = new Mock<IEditorTabFactory>();
        tabFactory.Setup(factory => factory.Create())
            .Returns(() => CreateTab(fileService, fileSystem, dialog.Object));
        tabFactory.Setup(factory => factory.CreateUntitled(It.IsAny<string>()))
            .Returns((string content) =>
            {
                var tab = CreateTab(fileService, fileSystem, dialog.Object);
                tab.RestoreTextSnapshot(
                    string.Empty,
                    "Untitled",
                    content,
                    isModified: false,
                    Encoding.Unicode.CodePage,
                    hasBom: true);
                return tab;
            });

        try
        {
            var settings = new SettingsService(fileSystem, settingsRoot);
            using var persister = CreateSessionCoordinator(
                settings,
                fileSystem,
                tabFactory.Object,
                snapshotRoot,
                "generation-a",
                "owner-a");
            var content = "\uFEFFdraft\0\r\n\uD800";
            using var original = CreateTab(fileService, fileSystem, dialog.Object);
            original.RestoreTextSnapshot(
                string.Empty,
                "Untitled-7",
                content,
                isModified: true,
                Encoding.Unicode.CodePage,
                hasBom: true);
            original.CursorOffset = 6;
            original.ScrollOffset = 12.5;
            var originalIdentity = original.AutoSaveIdentity;
            var closeLifecycle = CreateLifecycleCoordinator(() => Task.CompletedTask);
            await closeLifecycle.StartAsync(
                Array.Empty<string>(),
                hasAnotherRunningInstance: false,
                requestRecovery: () => throw new InvalidOperationException(
                    "Crash recovery should not be requested."));

            var close = await closeLifecycle.CloseAsync(
                beginPersistence: () => { },
                persistSession: () => persister.PersistShutdownSession(
                    new[] { original },
                    original),
                terminalShutdownTimeout: TimeSpan.FromSeconds(1));

            Assert.Equal(MainWindowCloseOutcome.ReadyToClose, close.Outcome);
            Assert.True(close.ShouldClose);
            dialog.VerifyNoOtherCalls();

            var nextSettings = new SettingsService(fileSystem, settingsRoot);
            using var restorer = CreateSessionCoordinator(
                nextSettings,
                fileSystem,
                tabFactory.Object,
                snapshotRoot,
                "generation-b",
                "owner-b");
            var adoptedTabs = new List<EditorTabViewModel>();
            EditorTabViewModel? selectedTab = null;
            var startupLifecycle = CreateLifecycleCoordinator(async () =>
            {
                using var restoredSession =
                    await restorer.RestoreShutdownSessionAsync();
                var adoption = restorer.AdoptRestoredTabs(
                    restoredSession,
                    adoptedTabs,
                    adoptedTabs.Add);
                selectedTab = adoption.SelectedTab;
            });

            var startup = await startupLifecycle.StartAsync(
                Array.Empty<string>(),
                hasAnotherRunningInstance: false,
                requestRecovery: () => throw new InvalidOperationException(
                    "Crash recovery should not be requested."));

            Assert.Equal(MainWindowStartupOutcome.Success, startup.Outcome);
            var adopted = Assert.Single(adoptedTabs);
            Assert.Same(adopted, selectedTab);
            Assert.Equal("Untitled-7", adopted.FileName);
            Assert.Equal(string.Empty, adopted.FilePath);
            Assert.Equal(content, adopted.Content);
            Assert.True(adopted.IsModified);
            Assert.Equal(originalIdentity, adopted.AutoSaveIdentity);
            Assert.Equal(6, adopted.CursorOffset);
            Assert.Equal(12.5, adopted.ScrollOffset);
            Assert.Equal(Encoding.Unicode.CodePage, adopted.FileEncoding.CodePage);
            Assert.True(adopted.HasBom);
            dialog.VerifyNoOtherCalls();

            foreach (var tab in adoptedTabs)
                tab.Dispose();
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static EditorTabViewModel CreateTab(
        IFileService fileService,
        IFileSystemService fileSystemService,
        IDialogService dialogService) =>
        new(fileService, fileSystemService, dialogService);

    private static DocumentSessionCoordinator CreateSessionCoordinator(
        SettingsService settings,
        IFileSystemService fileSystemService,
        IEditorTabFactory tabFactory,
        string snapshotRoot,
        string generation,
        string owner) =>
        new(
            settings,
            fileSystemService,
            tabFactory,
            settings,
            () => Guid.NewGuid().ToString("N"),
            () => Guid.NewGuid().ToString("N"),
            () => generation,
            snapshotRoot,
            () => owner);

    private static MainWindowLifecycleCoordinator CreateLifecycleCoordinator(
        Func<Task> restoreSessionAsync) =>
        new(
            Mock.Of<IAutoSaveService>(),
            new MainWindowLifecycleOperations(
                restoreSessionAsync,
                _ => Task.CompletedTask,
                () => null,
                _ => Task.CompletedTask,
                _ => new TabRecoveryResult(true, Array.Empty<string>()),
                () => Array.Empty<AutoSaveEntry>(),
                _ => Task.CompletedTask));
}
