using System.Windows.Input;
using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class ShortcutGestureFormatterTests
{
    [Theory]
    [InlineData(Key.OemPlus, ModifierKeys.Control, "Ctrl+Plus")]
    [InlineData(Key.OemMinus, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+Minus")]
    [InlineData(Key.OemTilde, ModifierKeys.Alt, "Alt+`")]
    [InlineData(Key.OemPipe, ModifierKeys.Control, "Ctrl+\\")]
    [InlineData(Key.OemComma, ModifierKeys.Control, "Ctrl+,")]
    [InlineData(Key.D5, ModifierKeys.Control | ModifierKeys.Alt, "Ctrl+Alt+5")]
    [InlineData(Key.S, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, "Ctrl+Alt+Shift+S")]
    public void TryFormat_ReturnsGesture(Key key, ModifierKeys modifiers, string expected)
    {
        var formatted = ShortcutGestureFormatter.TryFormat(key, modifiers, out var gesture);

        Assert.True(formatted);
        Assert.Equal(expected, gesture);
    }

    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightAlt)]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.RWin)]
    public void TryFormat_ModifierOnlyKey_ReturnsFalse(Key key)
    {
        var formatted = ShortcutGestureFormatter.TryFormat(key, ModifierKeys.Control, out var gesture);

        Assert.False(formatted);
        Assert.Equal(string.Empty, gesture);
    }
}
