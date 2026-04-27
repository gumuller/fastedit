using FastEdit.Services;
using FastEdit.Services.Interfaces;

namespace FastEdit.Tests;

public class TextToolApplicationPlannerTests
{
    [Fact]
    public void Create_FailedResult_ReturnsStatusOnlyPlan()
    {
        var plan = TextToolApplicationPlanner.Create(
            TextToolOperation.Base64Decode,
            TextToolResult.Fail("Invalid Base64 input"),
            hasSelection: true);

        Assert.Equal(TextToolApplicationTarget.StatusOnly, plan.Target);
        Assert.Null(plan.ReplacementText);
        Assert.Equal("Invalid Base64 input", plan.StatusText);
    }

    [Fact]
    public void Create_FailedResultWithoutMessage_UsesDefaultStatus()
    {
        var plan = TextToolApplicationPlanner.Create(
            TextToolOperation.UrlDecode,
            new TextToolResult(false, string.Empty),
            hasSelection: false);

        Assert.Equal(TextToolApplicationTarget.StatusOnly, plan.Target);
        Assert.Equal("Text tool failed", plan.StatusText);
    }

    [Fact]
    public void Create_ChecksumResult_ReturnsStatusOnlyPlan()
    {
        var plan = TextToolApplicationPlanner.Create(
            TextToolOperation.ComputeSha256,
            TextToolResult.Ok("abc123", "SHA-256: abc123"),
            hasSelection: false);

        Assert.Equal(TextToolApplicationTarget.StatusOnly, plan.Target);
        Assert.Null(plan.ReplacementText);
        Assert.Equal("SHA-256: abc123", plan.StatusText);
    }

    [Fact]
    public void Create_ChecksumResultWithoutMessage_UsesResultTextAsStatus()
    {
        var plan = TextToolApplicationPlanner.Create(
            TextToolOperation.ComputeMd5,
            TextToolResult.Ok("abc123"),
            hasSelection: true);

        Assert.Equal(TextToolApplicationTarget.StatusOnly, plan.Target);
        Assert.Equal("abc123", plan.StatusText);
    }

    [Fact]
    public void Create_MutatingResultWithSelection_ReturnsSelectionPlan()
    {
        var plan = TextToolApplicationPlanner.Create(
            TextToolOperation.UpperCase,
            TextToolResult.Ok("HELLO", "Converted to UPPERCASE"),
            hasSelection: true);

        Assert.Equal(TextToolApplicationTarget.Selection, plan.Target);
        Assert.Equal("HELLO", plan.ReplacementText);
        Assert.Equal("Converted to UPPERCASE", plan.StatusText);
    }

    [Fact]
    public void Create_MutatingResultWithoutSelection_ReturnsDocumentPlan()
    {
        var plan = TextToolApplicationPlanner.Create(
            TextToolOperation.LowerCase,
            TextToolResult.Ok("hello"),
            hasSelection: false);

        Assert.Equal(TextToolApplicationTarget.Document, plan.Target);
        Assert.Equal("hello", plan.ReplacementText);
        Assert.Null(plan.StatusText);
    }
}
