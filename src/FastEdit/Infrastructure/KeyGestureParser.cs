using System.ComponentModel;
using System.Windows.Input;

namespace FastEdit.Infrastructure;

public static class KeyGestureParser
{
    private static readonly IReadOnlyDictionary<string, ModifierKeys> ModifierAliases =
        new Dictionary<string, ModifierKeys>(StringComparer.OrdinalIgnoreCase)
        {
            ["CTRL"] = ModifierKeys.Control,
            ["CONTROL"] = ModifierKeys.Control,
            ["ALT"] = ModifierKeys.Alt,
            ["SHIFT"] = ModifierKeys.Shift,
        };

    private static readonly IReadOnlyDictionary<string, Key> KeyAliases =
        new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase)
        {
            ["+"] = Key.OemPlus,
            ["Plus"] = Key.OemPlus,
            ["-"] = Key.OemMinus,
            ["Minus"] = Key.OemMinus,
            ["`"] = Key.OemTilde,
            ["\\"] = Key.OemPipe,
            [","] = Key.OemComma,
            ["0"] = Key.D0,
            ["1"] = Key.D1,
            ["2"] = Key.D2,
            ["3"] = Key.D3,
            ["4"] = Key.D4,
            ["5"] = Key.D5,
            ["6"] = Key.D6,
            ["7"] = Key.D7,
            ["8"] = Key.D8,
            ["9"] = Key.D9,
        };

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
        ModifierAliases.TryGetValue(modifier, out var key)
            ? key
            : null;

    private static Key? ParseKey(string keyPart) =>
        KeyAliases.TryGetValue(keyPart, out var alias)
            ? alias
            : Enum.TryParse<Key>(keyPart, true, out var key) ? key : null;
}
