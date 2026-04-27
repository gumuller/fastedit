using FastEdit.Infrastructure;
using FastEdit.ViewModels;

namespace FastEdit.Tests;

public class EditorFeatureGatePolicyTests
{
    [Fact]
    public void Create_SmallTextFile_EnablesAllEditorFeatures()
    {
        var gate = EditorFeatureGatePolicy.Create(FileOpenMode.Text, fileSize: 1024);

        Assert.True(gate.SyntaxHighlightingEnabled);
        Assert.True(gate.FoldingEnabled);
        Assert.True(gate.MinimapEnabled);
        Assert.True(gate.IndentGuidesEnabled);
        Assert.True(gate.OccurrenceHighlightingEnabled);
        Assert.True(gate.CompletionEnabled);
        Assert.True(gate.BracketMatchingEnabled);
        Assert.Null(gate.StatusMessage);
    }

    [Fact]
    public void Create_TenMegabyteTextFile_DisablesOnlyMinimap()
    {
        var gate = EditorFeatureGatePolicy.Create(
            FileOpenMode.Text,
            EditorFeatureGatePolicy.DisableMinimapThresholdBytes);

        Assert.True(gate.SyntaxHighlightingEnabled);
        Assert.True(gate.FoldingEnabled);
        Assert.False(gate.MinimapEnabled);
        Assert.True(gate.IndentGuidesEnabled);
        Assert.True(gate.OccurrenceHighlightingEnabled);
        Assert.True(gate.CompletionEnabled);
        Assert.True(gate.BracketMatchingEnabled);
        Assert.Contains("minimap disabled", gate.StatusMessage);
    }

    [Fact]
    public void Create_FiftyMegabyteTextFile_DisablesExpensiveEditorFeatures()
    {
        var gate = EditorFeatureGatePolicy.Create(
            FileOpenMode.Text,
            EditorFeatureGatePolicy.DisableAdvancedFeaturesThresholdBytes);

        Assert.False(gate.SyntaxHighlightingEnabled);
        Assert.False(gate.FoldingEnabled);
        Assert.False(gate.MinimapEnabled);
        Assert.False(gate.IndentGuidesEnabled);
        Assert.False(gate.OccurrenceHighlightingEnabled);
        Assert.False(gate.CompletionEnabled);
        Assert.False(gate.BracketMatchingEnabled);
        Assert.Contains("disabled for performance", gate.StatusMessage);
    }

    [Theory]
    [InlineData(FileOpenMode.Binary)]
    [InlineData(FileOpenMode.LargeText)]
    public void Create_NonTextMode_DisablesEditorOnlyFeatures(FileOpenMode mode)
    {
        var gate = EditorFeatureGatePolicy.Create(mode, fileSize: 1024);

        Assert.False(gate.SyntaxHighlightingEnabled);
        Assert.False(gate.FoldingEnabled);
        Assert.False(gate.MinimapEnabled);
        Assert.False(gate.IndentGuidesEnabled);
        Assert.False(gate.OccurrenceHighlightingEnabled);
        Assert.False(gate.CompletionEnabled);
        Assert.False(gate.BracketMatchingEnabled);
    }
}
