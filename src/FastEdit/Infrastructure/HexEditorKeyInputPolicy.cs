using System.Windows.Input;

namespace FastEdit.Infrastructure;

public static class HexEditorKeyInputPolicy
{
    private static readonly IReadOnlyDictionary<Key, Func<NavigationContext, long>> NavigationOffsets =
        new Dictionary<Key, Func<NavigationContext, long>>
        {
            [Key.Left] = static context => Math.Max(0, context.SelectedOffset - 1),
            [Key.Right] = static context => Math.Min(context.BufferLength - 1, context.SelectedOffset + 1),
            [Key.Up] = static context => Math.Max(0, context.SelectedOffset - context.BytesPerRow),
            [Key.Down] = static context => Math.Min(context.BufferLength - 1, context.SelectedOffset + context.BytesPerRow),
            [Key.PageUp] = static context => Math.Max(0, context.SelectedOffset - context.VisibleRows * context.BytesPerRow),
            [Key.PageDown] = static context => Math.Min(context.BufferLength - 1, context.SelectedOffset + context.VisibleRows * context.BytesPerRow),
            [Key.Home] = static context => context.Modifiers.HasFlag(ModifierKeys.Control)
                ? 0
                : (context.SelectedOffset / context.BytesPerRow) * context.BytesPerRow,
            [Key.End] = static context => context.Modifiers.HasFlag(ModifierKeys.Control)
                ? context.BufferLength - 1
                : Math.Min(context.BufferLength - 1, ((context.SelectedOffset / context.BytesPerRow) + 1) * context.BytesPerRow - 1),
        };

    public static HexEditorKeyDecision Decide(
        Key key,
        ModifierKeys modifiers,
        bool isSearchVisible,
        bool isEditorInputSource,
        bool hasSelection,
        long selectedOffset,
        long bufferLength,
        int bytesPerRow,
        int visibleRows)
    {
        if (key == Key.F && modifiers == ModifierKeys.Control)
            return HexEditorKeyDecision.ShowSearch();

        if (key == Key.Escape && isSearchVisible)
            return HexEditorKeyDecision.HideSearch();

        if (!isEditorInputSource || !hasSelection)
            return HexEditorKeyDecision.None;

        if (TryGetNibble(key, out var nibble))
            return HexEditorKeyDecision.EditNibble(nibble);

        return TryGetNavigationOffset(key, modifiers, selectedOffset, bufferLength, bytesPerRow, visibleRows, out var nextOffset)
            ? HexEditorKeyDecision.MoveSelection(nextOffset)
            : HexEditorKeyDecision.None;
    }

    private static bool TryGetNibble(Key key, out int nibble)
    {
        if (key >= Key.D0 && key <= Key.D9)
        {
            nibble = key - Key.D0;
            return true;
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            nibble = key - Key.NumPad0;
            return true;
        }

        if (key >= Key.A && key <= Key.F)
        {
            nibble = 10 + (key - Key.A);
            return true;
        }

        nibble = -1;
        return false;
    }

    private static bool TryGetNavigationOffset(
        Key key,
        ModifierKeys modifiers,
        long selectedOffset,
        long bufferLength,
        int bytesPerRow,
        int visibleRows,
        out long nextOffset)
    {
        nextOffset = NavigationOffsets.TryGetValue(key, out var getOffset)
            ? getOffset(new NavigationContext(modifiers, selectedOffset, bufferLength, bytesPerRow, visibleRows))
            : -1;

        return nextOffset >= 0 && nextOffset != selectedOffset;
    }
}

internal readonly record struct NavigationContext(
    ModifierKeys Modifiers,
    long SelectedOffset,
    long BufferLength,
    int BytesPerRow,
    int VisibleRows);

public readonly record struct HexEditorKeyDecision(HexEditorKeyAction Action, int Nibble, long Offset)
{
    public static HexEditorKeyDecision None => new(HexEditorKeyAction.None, -1, -1);
    public static HexEditorKeyDecision ShowSearch() => new(HexEditorKeyAction.ShowSearch, -1, -1);
    public static HexEditorKeyDecision HideSearch() => new(HexEditorKeyAction.HideSearch, -1, -1);
    public static HexEditorKeyDecision EditNibble(int nibble) => new(HexEditorKeyAction.EditNibble, nibble, -1);
    public static HexEditorKeyDecision MoveSelection(long offset) => new(HexEditorKeyAction.MoveSelection, -1, offset);
}

public enum HexEditorKeyAction
{
    None,
    ShowSearch,
    HideSearch,
    EditNibble,
    MoveSelection,
}
