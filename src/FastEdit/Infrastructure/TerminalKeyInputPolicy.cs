using System.Windows.Input;

namespace FastEdit.Infrastructure;

public static class TerminalKeyInputPolicy
{
    public static TerminalKeyDecision Decide(
        Key key,
        ModifierKeys modifiers,
        bool hasActiveTab,
        bool runnerIsBusy,
        bool selectionIsEmpty,
        bool caretInInput,
        bool selectionInInput,
        bool caretAtInputStart)
    {
        if (!hasActiveTab)
            return TerminalKeyDecision.None;

        var context = new TerminalKeyContext(
            key,
            modifiers,
            runnerIsBusy,
            selectionIsEmpty,
            caretInInput,
            selectionInInput,
            caretAtInputStart);

        return DecideControlChord(context)
            ?? DecideCommandKey(context)
            ?? DecideProtectedEdit(context)
            ?? DecideCaretRepair(context);
    }

    private static TerminalKeyDecision? DecideControlChord(TerminalKeyContext context)
    {
        if (context is { Key: Key.C, Modifiers: ModifierKeys.Control })
            return context.SelectionIsEmpty && context.RunnerIsBusy
                ? TerminalKeyDecision.AsHandled(TerminalKeyAction.StopProcess)
                : TerminalKeyDecision.None;

        if (context is { Key: Key.V, Modifiers: ModifierKeys.Control })
            return context.CaretInInput
                ? TerminalKeyDecision.None
                : TerminalKeyDecision.AsUnhandled(TerminalKeyAction.MoveCaretToInputEnd);

        return context.Modifiers == ModifierKeys.Control ? TerminalKeyDecision.None : null;
    }

    private static TerminalKeyDecision? DecideCommandKey(TerminalKeyContext context) =>
        context.Key switch
        {
            Key.Enter => TerminalKeyDecision.AsHandled(TerminalKeyAction.SubmitInput),
            Key.Up => TerminalKeyDecision.AsHandled(TerminalKeyAction.PreviousHistory),
            Key.Down => TerminalKeyDecision.AsHandled(TerminalKeyAction.NextHistory),
            Key.Home => TerminalKeyDecision.AsHandled(TerminalKeyAction.MoveToInputStart),
            _ => null,
        };

    private static TerminalKeyDecision? DecideProtectedEdit(TerminalKeyContext context)
    {
        if ((context.Key == Key.Back || context.Key == Key.Delete) &&
            ShouldBlockEdit(context.CaretInInput, context.SelectionInInput, context.SelectionIsEmpty, context.CaretAtInputStart, context.Key))
            return TerminalKeyDecision.AsHandled(TerminalKeyAction.BlockEdit);

        return context is { Key: Key.Left, CaretAtInputStart: true }
            ? TerminalKeyDecision.AsHandled(TerminalKeyAction.BlockEdit)
            : null;
    }

    private static TerminalKeyDecision DecideCaretRepair(TerminalKeyContext context) =>
        context.CaretInInput
            ? TerminalKeyDecision.None
            : TerminalKeyDecision.AsUnhandled(TerminalKeyAction.MoveCaretToInputEnd);

    private static bool ShouldBlockEdit(
        bool caretInInput,
        bool selectionInInput,
        bool selectionIsEmpty,
        bool caretAtInputStart,
        Key key)
    {
        if (!caretInInput)
            return true;

        if (!selectionIsEmpty && !selectionInInput)
            return true;

        return key == Key.Back && caretAtInputStart;
    }
}

internal readonly record struct TerminalKeyContext(
    Key Key,
    ModifierKeys Modifiers,
    bool RunnerIsBusy,
    bool SelectionIsEmpty,
    bool CaretInInput,
    bool SelectionInInput,
    bool CaretAtInputStart);

public readonly record struct TerminalKeyDecision(TerminalKeyAction Action, bool Handled)
{
    public static TerminalKeyDecision None => new(TerminalKeyAction.None, false);
    public static TerminalKeyDecision AsHandled(TerminalKeyAction action) => new(action, true);
    public static TerminalKeyDecision AsUnhandled(TerminalKeyAction action) => new(action, false);
}

public enum TerminalKeyAction
{
    None,
    StopProcess,
    MoveCaretToInputEnd,
    SubmitInput,
    PreviousHistory,
    NextHistory,
    MoveToInputStart,
    BlockEdit,
}
