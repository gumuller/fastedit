using System.IO;
using FastEdit.Services;
using FastEdit.Services.Interfaces;
using FluentAssertions;
using Moq;

namespace FastEdit.Tests;

public class WorkspaceServiceTests
{
    private readonly Mock<IFileSystemService> _mockFs = new();
    private readonly WorkspaceService _sut;
    private readonly string _sessionsDir;

    public WorkspaceServiceTests()
    {
        _sessionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit", "Sessions");
        _sut = new WorkspaceService(_mockFs.Object);
    }

    // --- Named Sessions ---

    [Fact]
    public void GetSavedSessionNames_NoDirectory_ReturnsEmpty()
    {
        _mockFs.Setup(f => f.DirectoryExists(_sessionsDir)).Returns(false);
        _sut.GetSavedSessionNames().Should().BeEmpty();
    }

    [Fact]
    public void GetSavedSessionNames_WithFiles_ReturnsNames()
    {
        _mockFs.Setup(f => f.DirectoryExists(_sessionsDir)).Returns(true);
        _mockFs.Setup(f => f.GetFiles(_sessionsDir, "*.json", false)).Returns(new[]
        {
            Path.Combine(_sessionsDir, "Work.json"),
            Path.Combine(_sessionsDir, "Personal.json")
        });

        var names = _sut.GetSavedSessionNames();
        names.Should().BeEquivalentTo(new[] { "Personal", "Work" }); // sorted
    }

    [Fact]
    public void SaveNamedSession_WritesJsonFile()
    {
        var session = new SessionData
        {
            Files = new() { new SessionFileEntry { FilePath = "test.txt", IsUntitled = false } },
            ActiveTabIndex = 0
        };

        _sut.SaveNamedSession("MySession", session);

        _mockFs.Verify(f => f.CreateDirectory(_sessionsDir), Times.Once);
        _mockFs.Verify(f => f.WriteAllText(
            Path.Combine(_sessionsDir, "MySession.json"),
            It.Is<string>(s => s.Contains("MySession") && s.Contains("test.txt"))),
            Times.Once);
    }

    [Fact]
    public void LoadNamedSession_ValidFile_ReturnsSessionData()
    {
        var path = Path.Combine(_sessionsDir, "Test.json");
        var json = """{"Name":"Test","Files":[{"FilePath":"a.txt","IsUntitled":false}],"ActiveTabIndex":0,"SavedAt":"2024-01-01T00:00:00Z"}""";

        _mockFs.Setup(f => f.FileExists(path)).Returns(true);
        _mockFs.Setup(f => f.ReadAllText(path)).Returns(json);

        var session = _sut.LoadNamedSession("Test");
        session.Should().NotBeNull();
        session!.Name.Should().Be("Test");
        session.Files.Should().HaveCount(1);
        session.Files[0].FilePath.Should().Be("a.txt");
    }

    [Fact]
    public void LoadNamedSession_NotFound_ReturnsNull()
    {
        _mockFs.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);
        _sut.LoadNamedSession("Missing").Should().BeNull();
    }

    [Fact]
    public void DeleteNamedSession_DeletesFile()
    {
        var path = Path.Combine(_sessionsDir, "Old.json");
        _mockFs.Setup(f => f.FileExists(path)).Returns(true);

        _sut.DeleteNamedSession("Old");
        _mockFs.Verify(f => f.DeleteFile(path), Times.Once);
    }

    [Fact]
    public void DeleteNamedSession_NotFound_DoesNothing()
    {
        _mockFs.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);
        _sut.DeleteNamedSession("Ghost");
        _mockFs.Verify(f => f.DeleteFile(It.IsAny<string>()), Times.Never);
    }

    // --- Workspaces ---

    [Fact]
    public void SaveWorkspace_WritesJson()
    {
        var workspace = new WorkspaceData
        {
            Name = "Project",
            RootFolders = new() { @"C:\src", @"C:\docs" }
        };

        _sut.SaveWorkspace(@"C:\project.fastedit-workspace", workspace);

        _mockFs.Verify(f => f.WriteAllText(
            @"C:\project.fastedit-workspace",
            It.Is<string>(s => s.Contains("Project") && s.Contains(@"C:\\src"))),
            Times.Once);
    }

    [Fact]
    public void LoadWorkspace_ValidFile_ReturnsData()
    {
        var json = """{"Name":"Project","RootFolders":["C:\\src","C:\\docs"],"ActiveSession":null,"Settings":{}}""";
        _mockFs.Setup(f => f.FileExists(@"C:\p.fastedit-workspace")).Returns(true);
        _mockFs.Setup(f => f.ReadAllText(@"C:\p.fastedit-workspace")).Returns(json);

        var ws = _sut.LoadWorkspace(@"C:\p.fastedit-workspace");
        ws.Should().NotBeNull();
        ws!.Name.Should().Be("Project");
        ws.RootFolders.Should().HaveCount(2);
    }

    [Fact]
    public void LoadWorkspace_NotFound_ReturnsNull()
    {
        _mockFs.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);
        _sut.LoadWorkspace(@"C:\missing.fastedit-workspace").Should().BeNull();
    }

    [Fact]
    public void SaveNamedSession_SpecialCharsInName_Sanitized()
    {
        var session = new SessionData { Files = new() };
        _sut.SaveNamedSession("My<>Session", session);

        _mockFs.Verify(f => f.WriteAllText(
            Path.Combine(_sessionsDir, "My__Session.json"),
            It.IsAny<string>()), Times.Once);
    }
}
