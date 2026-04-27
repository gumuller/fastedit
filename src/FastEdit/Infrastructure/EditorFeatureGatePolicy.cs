using FastEdit.ViewModels;

namespace FastEdit.Infrastructure;

public sealed record EditorFeatureGate(
    bool SyntaxHighlightingEnabled,
    bool FoldingEnabled,
    bool MinimapEnabled,
    bool IndentGuidesEnabled,
    bool OccurrenceHighlightingEnabled,
    bool CompletionEnabled,
    bool BracketMatchingEnabled,
    string? StatusMessage);

public static class EditorFeatureGatePolicy
{
    public const long DisableMinimapThresholdBytes = 10L * 1024 * 1024;
    public const long DisableAdvancedFeaturesThresholdBytes = 50L * 1024 * 1024;

    public static EditorFeatureGate Create(FileOpenMode mode, long fileSize)
    {
        if (mode != FileOpenMode.Text)
        {
            return Disabled("Editor-only features are unavailable outside text mode.");
        }

        if (fileSize >= DisableAdvancedFeaturesThresholdBytes)
        {
            return Disabled("Large file: editor features disabled for performance.");
        }

        if (fileSize >= DisableMinimapThresholdBytes)
        {
            return new EditorFeatureGate(
                SyntaxHighlightingEnabled: true,
                FoldingEnabled: true,
                MinimapEnabled: false,
                IndentGuidesEnabled: true,
                OccurrenceHighlightingEnabled: true,
                CompletionEnabled: true,
                BracketMatchingEnabled: true,
                StatusMessage: "Large file: minimap disabled for performance.");
        }

        return new EditorFeatureGate(
            SyntaxHighlightingEnabled: true,
            FoldingEnabled: true,
            MinimapEnabled: true,
            IndentGuidesEnabled: true,
            OccurrenceHighlightingEnabled: true,
            CompletionEnabled: true,
            BracketMatchingEnabled: true,
            StatusMessage: null);
    }

    private static EditorFeatureGate Disabled(string statusMessage) =>
        new(
            SyntaxHighlightingEnabled: false,
            FoldingEnabled: false,
            MinimapEnabled: false,
            IndentGuidesEnabled: false,
            OccurrenceHighlightingEnabled: false,
            CompletionEnabled: false,
            BracketMatchingEnabled: false,
            StatusMessage: statusMessage);
}
