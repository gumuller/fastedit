using System.Windows.Input;

namespace FastEdit.Infrastructure;

public static class ShortcutGestureFormatter
{
    private static readonly ISet<Key> ModifierOnlyKeys = new HashSet<Key>
    {
        Key.LeftCtrl,
        Key.RightCtrl,
        Key.LeftAlt,
        Key.RightAlt,
        Key.LeftShift,
        Key.RightShift,
        Key.LWin,
        Key.RWin,
    };

    private static readonly IReadOnlyDictionary<Key, string> KeyNames =
        new Dictionary<Key, string>
        {
            [Key.OemPlus] = "Plus",
            [Key.OemMinus] = "Minus",
            [Key.OemTilde] = "`",
            [Key.OemPipe] = "\\",
            [Key.OemComma] = ",",
            [Key.D0] = "0",
            [Key.D1] = "1",
            [Key.D2] = "2",
            [Key.D3] = "3",
            [Key.D4] = "4",
            [Key.D5] = "5",
            [Key.D6] = "6",
            [Key.D7] = "7",
            [Key.D8] = "8",
            [Key.D9] = "9",
        };

    public static bool TryFormat(Key key, ModifierKeys modifiers, out string gesture)
    {
        if (ModifierOnlyKeys.Contains(key))
        {
            gesture = string.Empty;
            return false;
        }

        var parts = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(KeyNames.TryGetValue(key, out var keyName) ? keyName : key.ToString());

        gesture = string.Join("+", parts);
        return true;
    }
}
