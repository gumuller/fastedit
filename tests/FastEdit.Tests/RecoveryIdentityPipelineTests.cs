using System.IO;
using System.IO.Enumeration;
using FastEdit.Infrastructure;
using FastEdit.Services;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;
using Moq;

namespace FastEdit.Tests;

public class RecoveryIdentityPipelineTests
{
    [Fact]
    public void DivergentGenerations_RekeyAndRoundTripWithoutContentLoss()
    {
        var autoSaveDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit",
            "AutoSave");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(autoSaveDirectory, "manifest-a.json")] =
                Manifest("a.txt", "shared"),
            [Path.Combine(autoSaveDirectory, "manifest-b.json")] =
                Manifest("b.txt", "shared"),
            [Path.Combine(autoSaveDirectory, "a.txt")] = "first edit",
            [Path.Combine(autoSaveDirectory, "b.txt")] = "second edit"
        };
        var fileSystem = CreateFileSystem(files);
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(service => service.AutoSaveIntervalSeconds).Returns(60);
        var tabFactory = new Mock<IEditorTabFactory>();
        var fileService = new Mock<IFileService>();
        var dialogService = new Mock<IDialogService>();
        tabFactory.Setup(factory => factory.CreateUntitled(It.IsAny<string?>()))
            .Returns((string? content) =>
            {
                var tab = new EditorTabViewModel(
                    fileService.Object,
                    fileSystem.Object,
                    dialogService.Object);
                tab.SetContentBaseline(content ?? string.Empty, isModified: true);
                return tab;
            });
        var generatedIdentities = new Queue<string>(new[] { "rekeyed" });
        var coordinator = new DocumentSessionCoordinator(
            settings.Object,
            fileSystem.Object,
            tabFactory.Object,
            generatedIdentities.Dequeue);
        var tabs = new List<EditorTabViewModel>();
        var autoSave = new AutoSaveService(
            fileSystem.Object,
            settings.Object,
            new InlineDispatcherService());

        var attempt = CrashRecoveryCoordinator.Recover(
            autoSave,
            entries =>
            {
                var recovery = coordinator.RecoverTabs(entries, tabs);
                tabs.AddRange(recovery.RecoveredTabs!);
                return recovery;
            },
            () => coordinator.CreateAutoSaveEntries(tabs));

        Assert.True(attempt.Success);
        Assert.Equal(2, tabs.Count);
        Assert.Equal(
            new[] { "first edit", "second edit" },
            tabs.Select(tab => tab.Content).Order().ToArray());
        Assert.Equal(2, tabs.Select(tab => tab.AutoSaveIdentity).Distinct().Count());
        Assert.Contains(tabs, tab => tab.AutoSaveIdentity == "shared");
        Assert.Contains(tabs, tab => tab.AutoSaveIdentity == "rekeyed");
        var replacementEntries = coordinator.CreateAutoSaveEntries(tabs);
        Assert.Equal(2, replacementEntries.Select(entry => entry.Id).Distinct().Count());

        var subsequentAutoSave = new AutoSaveService(
            fileSystem.Object,
            settings.Object,
            new InlineDispatcherService());
        var subsequentEntries = subsequentAutoSave.GetRecoveryEntries();
        var subsequentTabs = new List<EditorTabViewModel>();
        var subsequentRecovery = coordinator.RecoverTabs(
            subsequentEntries.Entries,
            subsequentTabs);
        subsequentTabs.AddRange(subsequentRecovery.RecoveredTabs!);

        Assert.True(subsequentEntries.Success);
        Assert.True(subsequentRecovery.Success);
        Assert.Equal(2, subsequentTabs.Count);
        Assert.Equal(
            new[] { "first edit", "second edit" },
            subsequentTabs.Select(tab => tab.Content).Order().ToArray());
        Assert.Equal(
            2,
            subsequentTabs.Select(tab => tab.AutoSaveIdentity).Distinct().Count());
    }

    private static string Manifest(string contentFile, string tabIdentity) =>
        $$"""
          [{
            "Id":"tab-{{tabIdentity}}",
            "TabIdentity":"{{tabIdentity}}",
            "FileName":"Untitled-1",
            "FilePath":null,
            "ContentFile":"{{contentFile}}",
            "IsUntitled":true,
            "CursorOffset":0,
            "ScrollOffset":0
          }]
          """;

    private static Mock<IFileSystemService> CreateFileSystem(
        IDictionary<string, string> files)
    {
        var fileSystem = new Mock<IFileSystemService>();
        fileSystem.Setup(service => service.DirectoryExists(It.IsAny<string>()))
            .Returns(true);
        fileSystem.Setup(service => service.CreateDirectory(It.IsAny<string>()));
        fileSystem.Setup(service => service.FileExists(It.IsAny<string>()))
            .Returns((string path) => files.ContainsKey(path));
        fileSystem.Setup(service => service.ReadAllText(It.IsAny<string>()))
            .Returns((string path) => files[path]);
        fileSystem.Setup(service => service.WriteAllTextAtomic(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback<string, string>((path, content) => files[path] = content);
        fileSystem.Setup(service => service.DeleteFile(It.IsAny<string>()))
            .Callback<string>(path => files.Remove(path));
        fileSystem.Setup(service => service.MoveFile(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>()))
            .Callback<string, string, bool>((source, destination, overwrite) =>
            {
                if (!overwrite && files.ContainsKey(destination))
                    throw new IOException($"Destination already exists: {destination}");
                files[destination] = files[source];
                files.Remove(source);
            });
        fileSystem.Setup(service => service.GetFiles(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>()))
            .Returns((string directory, string pattern, bool _) =>
                files.Keys
                    .Where(path =>
                        string.Equals(
                            Path.GetDirectoryName(path),
                            directory,
                            StringComparison.OrdinalIgnoreCase) &&
                        FileSystemName.MatchesSimpleExpression(
                            pattern,
                            Path.GetFileName(path),
                            ignoreCase: true))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        return fileSystem;
    }

    private sealed class InlineDispatcherService : IDispatcherService
    {
        public void Invoke(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> func) =>
            Task.FromResult(func());

        public bool CheckAccess() => true;
    }
}
