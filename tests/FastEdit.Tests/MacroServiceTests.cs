using FastEdit.Services;
using FastEdit.Services.Interfaces;
using FluentAssertions;

namespace FastEdit.Tests;

public class MacroServiceTests
{
    private readonly MacroService _sut = new();

    [Fact]
    public void IsRecording_InitiallyFalse()
    {
        _sut.IsRecording.Should().BeFalse();
    }

    [Fact]
    public void StartRecording_SetsIsRecording()
    {
        _sut.StartRecording();
        _sut.IsRecording.Should().BeTrue();
    }

    [Fact]
    public void StopRecording_ClearsIsRecording()
    {
        _sut.StartRecording();
        _sut.StopRecording();
        _sut.IsRecording.Should().BeFalse();
    }

    [Fact]
    public void RecordStep_WhileRecording_AddsStep()
    {
        _sut.StartRecording();
        _sut.RecordStep(new MacroStep(MacroAction.TypeText, "a"));
        _sut.RecordedStepCount.Should().Be(1);
    }

    [Fact]
    public void RecordStep_NotRecording_Ignored()
    {
        _sut.RecordStep(new MacroStep(MacroAction.TypeText, "a"));
        _sut.RecordedStepCount.Should().Be(0);
    }

    [Fact]
    public void RecordStep_MergesConsecutiveTypeText()
    {
        _sut.StartRecording();
        _sut.RecordStep(new MacroStep(MacroAction.TypeText, "h"));
        _sut.RecordStep(new MacroStep(MacroAction.TypeText, "i"));
        _sut.RecordedStepCount.Should().Be(1);
        _sut.GetRecordedSteps()[0].Parameter.Should().Be("hi");
    }

    [Fact]
    public void RecordStep_DoesNotMergeDifferentActions()
    {
        _sut.StartRecording();
        _sut.RecordStep(new MacroStep(MacroAction.TypeText, "a"));
        _sut.RecordStep(new MacroStep(MacroAction.NewLine));
        _sut.RecordStep(new MacroStep(MacroAction.TypeText, "b"));
        _sut.RecordedStepCount.Should().Be(3);
    }

    [Fact]
    public void StartRecording_ClearsPreviousMacro()
    {
        _sut.StartRecording();
        _sut.RecordStep(new MacroStep(MacroAction.TypeText, "old"));
        _sut.StopRecording();

        _sut.StartRecording();
        _sut.RecordedStepCount.Should().Be(0);
    }

    [Fact]
    public void HasMacro_FalseWhenEmpty()
    {
        _sut.HasMacro.Should().BeFalse();
    }

    [Fact]
    public void HasMacro_TrueAfterRecording()
    {
        _sut.StartRecording();
        _sut.RecordStep(new MacroStep(MacroAction.DuplicateLine));
        _sut.StopRecording();
        _sut.HasMacro.Should().BeTrue();
    }

    [Fact]
    public void ClearMacro_RemovesAllSteps()
    {
        _sut.StartRecording();
        _sut.RecordStep(new MacroStep(MacroAction.TypeText, "test"));
        _sut.StopRecording();

        _sut.ClearMacro();
        _sut.HasMacro.Should().BeFalse();
        _sut.RecordedStepCount.Should().Be(0);
    }

    [Fact]
    public void GetRecordedSteps_ReturnsCorrectSteps()
    {
        _sut.StartRecording();
        _sut.RecordStep(new MacroStep(MacroAction.TypeText, "hello"));
        _sut.RecordStep(new MacroStep(MacroAction.NewLine));
        _sut.RecordStep(new MacroStep(MacroAction.DuplicateLine));
        _sut.StopRecording();

        var steps = _sut.GetRecordedSteps();
        steps.Should().HaveCount(3);
        steps[0].Action.Should().Be(MacroAction.TypeText);
        steps[0].Parameter.Should().Be("hello");
        steps[1].Action.Should().Be(MacroAction.NewLine);
        steps[2].Action.Should().Be(MacroAction.DuplicateLine);
    }

    [Fact]
    public void RecordStep_TextTool_RecordsWithParameter()
    {
        _sut.StartRecording();
        _sut.RecordStep(new MacroStep(MacroAction.TextTool, "UpperCase"));
        _sut.StopRecording();

        var steps = _sut.GetRecordedSteps();
        steps.Should().HaveCount(1);
        steps[0].Action.Should().Be(MacroAction.TextTool);
        steps[0].Parameter.Should().Be("UpperCase");
    }
}
