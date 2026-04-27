using System.Windows.Input;
using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class KeyGestureParserTests
{
    [Fact]
    public void Parse_CtrlS_ReturnsControlS()
    {
        var gesture = KeyGestureParser.Parse("Ctrl+S");

        Assert.NotNull(gesture);
        Assert.Equal(Key.S, gesture.Key);
        Assert.Equal(ModifierKeys.Control, gesture.Modifiers);
    }

    [Fact]
    public void Parse_CtrlShiftS_ReturnsControlShiftS()
    {
        var gesture = KeyGestureParser.Parse("Ctrl+Shift+S");

        Assert.NotNull(gesture);
        Assert.Equal(Key.S, gesture.Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, gesture.Modifiers);
    }

    [Fact]
    public void Parse_CtrlPlusSymbol_ReturnsControlOemPlus()
    {
        var gesture = KeyGestureParser.Parse("Ctrl++");

        Assert.NotNull(gesture);
        Assert.Equal(Key.OemPlus, gesture.Key);
        Assert.Equal(ModifierKeys.Control, gesture.Modifiers);
    }

    [Fact]
    public void Parse_CtrlPlusWord_ReturnsControlOemPlus()
    {
        var gesture = KeyGestureParser.Parse("Ctrl+Plus");

        Assert.NotNull(gesture);
        Assert.Equal(Key.OemPlus, gesture.Key);
        Assert.Equal(ModifierKeys.Control, gesture.Modifiers);
    }

    [Fact]
    public void Parse_CtrlMinusSymbol_ReturnsControlOemMinus()
    {
        var gesture = KeyGestureParser.Parse("Ctrl+-");

        Assert.NotNull(gesture);
        Assert.Equal(Key.OemMinus, gesture.Key);
        Assert.Equal(ModifierKeys.Control, gesture.Modifiers);
    }

    [Fact]
    public void Parse_InvalidKey_ReturnsNull()
    {
        Assert.Null(KeyGestureParser.Parse("Ctrl+NotAKey"));
    }

    [Fact]
    public void Parse_UnknownModifier_ReturnsNull()
    {
        Assert.Null(KeyGestureParser.Parse("Meta+S"));
    }

    [Fact]
    public void Parse_IncompletePlusGesture_ReturnsNull()
    {
        Assert.Null(KeyGestureParser.Parse("Ctrl+"));
    }
}
