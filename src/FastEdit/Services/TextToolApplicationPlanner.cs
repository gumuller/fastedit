using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public enum TextToolApplicationTarget
{
    StatusOnly,
    Selection,
    Document
}

public sealed record TextToolApplicationPlan(
    TextToolApplicationTarget Target,
    string? ReplacementText,
    string? StatusText);

public static class TextToolApplicationPlanner
{
    public static TextToolApplicationPlan Create(
        TextToolOperation operation,
        TextToolResult result,
        bool hasSelection)
    {
        if (!result.Success)
        {
            return new TextToolApplicationPlan(
                TextToolApplicationTarget.StatusOnly,
                ReplacementText: null,
                StatusText: result.Message ?? "Text tool failed");
        }

        if (operation.IsChecksum())
        {
            return new TextToolApplicationPlan(
                TextToolApplicationTarget.StatusOnly,
                ReplacementText: null,
                StatusText: result.Message ?? result.Text);
        }

        return new TextToolApplicationPlan(
            hasSelection ? TextToolApplicationTarget.Selection : TextToolApplicationTarget.Document,
            ReplacementText: result.Text,
            StatusText: result.Message);
    }
}
