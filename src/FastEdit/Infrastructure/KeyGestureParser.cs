using System.ComponentModel;
using System.Windows.Input;

namespace FastEdit.Infrastructure;

public static class KeyGestureParser
{
    public static KeyGesture? Parse(string gestureString)
    {
        if (string.IsNullOrWhiteSpace(gestureString))
        {
            return null;
        }

        var parts = gestureString.Split('+');
        var keyPart = parts[^1].Trim();
        var modifierPartCount = parts.Length - 1;

        if (keyPart.Length == 0)
        {
            if (parts.Length < 3 || !string.IsNullOrWhiteSpace(parts[^2]))
            {
                return null;
            }

            keyPart = "+";
            modifierPartCount = parts.Length - 2;
        }

        var modifiers = ModifierKeys.None;
        for (var i = 0; i < modifierPartCount; i++)
        {
            var modifier = ParseModifier(parts[i].Trim());
            if (modifier is null)
            {
                return null;
            }

            modifiers |= modifier.Value;
        }

        var key = ParseKey(keyPart);
        if (key is null or Key.None)
        {
            return null;
        }

        try
        {
            return new KeyGesture(key.Value, modifiers);
        }
        catch (InvalidEnumArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static ModifierKeys? ParseModifier(string modifier) =>
        modifier.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => ModifierKeys.Control,
            "ALT" => ModifierKeys.Alt,
            "SHIFT" => ModifierKeys.Shift,
            _ => null
        };

    private static Key? ParseKey(string keyPart) =>
        keyPart switch
        {
            "+" or "Plus" => Key.OemPlus,
            "-" or "Minus" => Key.OemMinus,
            "`" => Key.OemTilde,
            "\\" => Key.OemPipe,
            "," => Key.OemComma,
            "0" => Key.D0,
            "1" => Key.D1,
            "2" => Key.D2,
            "3" => Key.D3,
            "4" => Key.D4,
            "5" => Key.D5,
            "6" => Key.D6,
            "7" => Key.D7,
            "8" => Key.D8,
            "9" => Key.D9,
            _ => Enum.TryParse<Key>(keyPart, true, out var key) ? key : null
        };
}
