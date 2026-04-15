namespace FastEdit.Services.Interfaces;

public interface IMacroService
{
    bool IsRecording { get; }
    bool HasMacro { get; }
    int RecordedStepCount { get; }

    void StartRecording();
    void StopRecording();
    void RecordStep(MacroStep step);
    IReadOnlyList<MacroStep> GetRecordedSteps();
    void ClearMacro();
}

public record MacroStep(MacroAction Action, string? Parameter = null);

public enum MacroAction
{
    TypeText,
    DeleteForward,
    DeleteBackward,
    NewLine,
    DuplicateLine,
    MoveLineUp,
    MoveLineDown,
    ToUpperCase,
    ToLowerCase,
    TrimTrailing,
    RemoveDuplicateLines,
    SortLinesAsc,
    SortLinesDesc,
    TabsToSpaces,
    SpacesToTabs,
    FormatDocument,
    MinifyDocument,
    TextTool
}
