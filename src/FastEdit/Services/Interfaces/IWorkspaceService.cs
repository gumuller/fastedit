namespace FastEdit.Services.Interfaces;

public interface IWorkspaceService
{
    // Named Sessions
    List<string> GetSavedSessionNames();
    void SaveNamedSession(string name, SessionData session);
    SessionData? LoadNamedSession(string name);
    void DeleteNamedSession(string name);

    // Workspaces
    WorkspaceData? LoadWorkspace(string filePath);
    void SaveWorkspace(string filePath, WorkspaceData workspace);
}

public class SessionData
{
    public string Name { get; set; } = "";
    public List<SessionFileEntry> Files { get; set; } = new();
    public int ActiveTabIndex { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
}

public class SessionFileEntry
{
    public string FilePath { get; set; } = "";
    public bool IsUntitled { get; set; }
    public string? Content { get; set; }
    public int CursorOffset { get; set; }
    public double ScrollOffset { get; set; }
}

public class WorkspaceData
{
    public string Name { get; set; } = "";
    public List<string> RootFolders { get; set; } = new();
    public string? ActiveSession { get; set; }
    public Dictionary<string, string> Settings { get; set; } = new();
}
