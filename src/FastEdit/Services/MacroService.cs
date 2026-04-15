using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public class MacroService : IMacroService
{
    private readonly List<MacroStep> _steps = new();

    public bool IsRecording { get; private set; }
    public bool HasMacro => _steps.Count > 0;
    public int RecordedStepCount => _steps.Count;

    public void StartRecording()
    {
        _steps.Clear();
        IsRecording = true;
    }

    public void StopRecording()
    {
        IsRecording = false;
    }

    public void RecordStep(MacroStep step)
    {
        if (!IsRecording) return;

        // Merge consecutive TypeText steps
        if (step.Action == MacroAction.TypeText && _steps.Count > 0)
        {
            var last = _steps[^1];
            if (last.Action == MacroAction.TypeText)
            {
                _steps[^1] = new MacroStep(MacroAction.TypeText, last.Parameter + step.Parameter);
                return;
            }
        }

        _steps.Add(step);
    }

    public IReadOnlyList<MacroStep> GetRecordedSteps() => _steps.AsReadOnly();

    public void ClearMacro()
    {
        _steps.Clear();
    }
}
