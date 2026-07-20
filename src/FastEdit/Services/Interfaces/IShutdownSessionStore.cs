namespace FastEdit.Services.Interfaces;

public interface IShutdownSessionStore
{
    ShutdownSessionState ReadShutdownSession(
        Action<ShutdownSessionState>? whileLocked = null);

    ShutdownSessionPublication PublishShutdownSession(
        ShutdownSessionState session,
        Action<ShutdownSessionPublication>? whileLocked = null);
}

public sealed record ShutdownSessionState(
    IReadOnlyList<SessionFile> Files,
    int ActiveTabIndex,
    IReadOnlyCollection<string>? ReplacedOwners = null);

public sealed record ShutdownSessionPublication(
    ShutdownSessionState PreviousSession,
    ShutdownSessionState PublishedSession);
