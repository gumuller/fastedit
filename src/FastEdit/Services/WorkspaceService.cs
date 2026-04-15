using System.IO;
using System.Text.Json;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public class WorkspaceService : IWorkspaceService
{
    private readonly IFileSystemService _fileSystem;
    private readonly string _sessionsDir;

    public WorkspaceService(IFileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
        _sessionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit", "Sessions");
    }

    // Named Sessions

    public List<string> GetSavedSessionNames()
    {
        if (!_fileSystem.DirectoryExists(_sessionsDir)) return new();

        return _fileSystem.GetFiles(_sessionsDir, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(n => n)
            .ToList();
    }

    public void SaveNamedSession(string name, SessionData session)
    {
        _fileSystem.CreateDirectory(_sessionsDir);
        session.Name = name;
        session.SavedAt = DateTime.UtcNow;

        var path = Path.Combine(_sessionsDir, $"{SanitizeFileName(name)}.json");
        var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
        _fileSystem.WriteAllText(path, json);
    }

    public SessionData? LoadNamedSession(string name)
    {
        var path = Path.Combine(_sessionsDir, $"{SanitizeFileName(name)}.json");
        if (!_fileSystem.FileExists(path)) return null;

        try
        {
            var json = _fileSystem.ReadAllText(path);
            return JsonSerializer.Deserialize<SessionData>(json);
        }
        catch
        {
            return null;
        }
    }

    public void DeleteNamedSession(string name)
    {
        var path = Path.Combine(_sessionsDir, $"{SanitizeFileName(name)}.json");
        if (_fileSystem.FileExists(path))
            _fileSystem.DeleteFile(path);
    }

    // Workspaces

    public WorkspaceData? LoadWorkspace(string filePath)
    {
        if (!_fileSystem.FileExists(filePath)) return null;

        try
        {
            var json = _fileSystem.ReadAllText(filePath);
            return JsonSerializer.Deserialize<WorkspaceData>(json);
        }
        catch
        {
            return null;
        }
    }

    public void SaveWorkspace(string filePath, WorkspaceData workspace)
    {
        var json = JsonSerializer.Serialize(workspace, new JsonSerializerOptions { WriteIndented = true });
        _fileSystem.WriteAllText(filePath, json);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
