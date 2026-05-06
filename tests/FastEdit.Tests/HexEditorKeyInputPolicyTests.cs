using System.Windows.Input;
using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class HexEditorKeyInputPolicyTests
{
    [Fact]
    public void Decide_CtrlF_ShowsSearch()
    {
        var decision = Decide(Key.F, ModifierKeys.Control);

        Assert.Equal(HexEditorKeyAction.ShowSearch, decision.Action);
    }

    [Fact]
    public void Decide_EscapeWithVisibleSearch_HidesSearch()
    {
        var decision = Decide(Key.Escape, ModifierKeys.None, isSearchVisible: true);

        Assert.Equal(HexEditorKeyAction.HideSearch, decision.Action);
    }

    [Fact]
    public void Decide_ChildControlInput_IgnoresHexEdit()
    {
        var decision = Decide(Key.A, ModifierKeys.None, isEditorInputSource: false);

        Assert.Equal(HexEditorKeyAction.None, decision.Action);
    }

    [Theory]
    [InlineData(Key.D0, 0)]
    [InlineData(Key.NumPad9, 9)]
    [InlineData(Key.A, 10)]
    [InlineData(Key.F, 15)]
    public void Decide_HexKey_ReturnsNibble(Key key, int expectedNibble)
    {
        var decision = Decide(key, ModifierKeys.None);

        Assert.Equal(HexEditorKeyAction.EditNibble, decision.Action);
        Assert.Equal(expectedNibble, decision.Nibble);
    }

    [Theory]
    [InlineData(Key.Left, ModifierKeys.None, 9)]
    [InlineData(Key.Right, ModifierKeys.None, 11)]
    [InlineData(Key.Up, ModifierKeys.None, 2)]
    [InlineData(Key.Down, ModifierKeys.None, 18)]
    [InlineData(Key.PageUp, ModifierKeys.None, 0)]
    [InlineData(Key.PageDown, ModifierKeys.None, 34)]
    [InlineData(Key.Home, ModifierKeys.None, 8)]
    [InlineData(Key.End, ModifierKeys.None, 15)]
    [InlineData(Key.Home, ModifierKeys.Control, 0)]
    [InlineData(Key.End, ModifierKeys.Control, 39)]
    public void Decide_NavigationKey_ReturnsTargetOffset(Key key, ModifierKeys modifiers, long expectedOffset)
    {
        var decision = Decide(key, modifiers);

        Assert.Equal(HexEditorKeyAction.MoveSelection, decision.Action);
        Assert.Equal(expectedOffset, decision.Offset);
    }

    private static HexEditorKeyDecision Decide(
        Key key,
        ModifierKeys modifiers,
        bool isSearchVisible = false,
        bool isEditorInputSource = true) =>
        HexEditorKeyInputPolicy.Decide(
            key,
            modifiers,
            isSearchVisible,
            isEditorInputSource,
            hasSelection: true,
            selectedOffset: 10,
            bufferLength: 40,
            bytesPerRow: 8,
            visibleRows: 3);
}
