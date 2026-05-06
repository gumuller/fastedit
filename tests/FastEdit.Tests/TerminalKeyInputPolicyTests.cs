using System.Windows.Input;
using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class TerminalKeyInputPolicyTests
{
    [Fact]
    public void Decide_NoActiveTab_ReturnsNone()
    {
        var decision = Decide(Key.Enter, hasActiveTab: false);

        Assert.Equal(TerminalKeyAction.None, decision.Action);
    }

    [Fact]
    public void Decide_CtrlCWithBusyRunnerAndEmptySelection_StopsProcess()
    {
        var decision = Decide(Key.C, ModifierKeys.Control, runnerIsBusy: true, selectionIsEmpty: true);

        Assert.Equal(TerminalKeyAction.StopProcess, decision.Action);
        Assert.True(decision.Handled);
    }

    [Fact]
    public void Decide_CtrlCWithSelection_AllowsDefaultCopy()
    {
        var decision = Decide(Key.C, ModifierKeys.Control, runnerIsBusy: true, selectionIsEmpty: false);

        Assert.Equal(TerminalKeyAction.None, decision.Action);
    }

    [Fact]
    public void Decide_CtrlVOutsideInput_MovesCaretWithoutHandlingPaste()
    {
        var decision = Decide(Key.V, ModifierKeys.Control, caretInInput: false);

        Assert.Equal(TerminalKeyAction.MoveCaretToInputEnd, decision.Action);
        Assert.False(decision.Handled);
    }

    [Theory]
    [InlineData(Key.Enter, TerminalKeyAction.SubmitInput)]
    [InlineData(Key.Up, TerminalKeyAction.PreviousHistory)]
    [InlineData(Key.Down, TerminalKeyAction.NextHistory)]
    [InlineData(Key.Home, TerminalKeyAction.MoveToInputStart)]
    public void Decide_CommandKeys_ReturnHandledActions(Key key, TerminalKeyAction expectedAction)
    {
        var decision = Decide(key);

        Assert.Equal(expectedAction, decision.Action);
        Assert.True(decision.Handled);
    }

    [Theory]
    [InlineData(Key.Back, false, true, true)]
    [InlineData(Key.Back, true, true, true)]
    [InlineData(Key.Delete, false, true, false)]
    [InlineData(Key.Delete, true, false, false)]
    [InlineData(Key.Left, true, true, true)]
    public void Decide_ProtectedEdit_ReturnsBlockEdit(
        Key key,
        bool caretInInput,
        bool selectionIsEmpty,
        bool caretAtInputStart)
    {
        var decision = Decide(
            key,
            caretInInput: caretInInput,
            selectionIsEmpty: selectionIsEmpty,
            selectionInInput: false,
            caretAtInputStart: caretAtInputStart);

        Assert.Equal(TerminalKeyAction.BlockEdit, decision.Action);
        Assert.True(decision.Handled);
    }

    private static TerminalKeyDecision Decide(
        Key key,
        ModifierKeys modifiers = ModifierKeys.None,
        bool hasActiveTab = true,
        bool runnerIsBusy = false,
        bool selectionIsEmpty = true,
        bool caretInInput = true,
        bool selectionInInput = true,
        bool caretAtInputStart = false) =>
        TerminalKeyInputPolicy.Decide(
            key,
            modifiers,
            hasActiveTab,
            runnerIsBusy,
            selectionIsEmpty,
            caretInInput,
            selectionInInput,
            caretAtInputStart);
}
