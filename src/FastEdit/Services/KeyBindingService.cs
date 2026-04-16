using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public class KeyBindingService : IKeyBindingService
{
    private readonly string _bindingsPath;
    private Dictionary<string, string> _bindings;

    private static readonly Dictionary<string, string> DefaultBindings = new()
    {
        ["NewFile"] = "Ctrl+N",
        ["OpenFile"] = "Ctrl+O",
        ["Save"] = "Ctrl+S",
        ["SaveAs"] = "Ctrl+Shift+S",
        ["CloseTab"] = "Ctrl+W",
        ["Find"] = "Ctrl+F",
        ["Replace"] = "Ctrl+H",
        ["GoToLine"] = "Ctrl+G",
        ["DuplicateLine"] = "Ctrl+Shift+D",
        ["MoveLineUp"] = "Alt+Up",
        ["MoveLineDown"] = "Alt+Down",
        ["ZoomIn"] = "Ctrl+Plus",
        ["ZoomOut"] = "Ctrl+Minus",
        ["ResetZoom"] = "Ctrl+0",
        ["FindInFiles"] = "Ctrl+Shift+F",
        ["ToggleBookmark"] = "Ctrl+F2",
        ["NextBookmark"] = "F2",
        ["PrevBookmark"] = "Shift+F2",
        ["CommandPalette"] = "Ctrl+Shift+P",
        ["Completion"] = "Ctrl+Space",
        ["ToggleTerminal"] = "Ctrl+`",
        ["SplitView"] = "Ctrl+\\",
        ["Print"] = "Ctrl+P",
        ["SelectNextOccurrence"] = "Ctrl+D",
        ["SelectAllOccurrences"] = "Ctrl+Shift+L",
        ["Settings"] = "Ctrl+,",
        ["ZenMode"] = "F11",
        ["ToggleExplorer"] = "Ctrl+B",
        ["ToggleFilterPanel"] = "Ctrl+L"
    };

    public KeyBindingService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit");
        Directory.CreateDirectory(appDataPath);
        _bindingsPath = Path.Combine(appDataPath, "keybindings.json");
        _bindings = LoadBindings();
    }

    public Dictionary<string, string> GetBindings()
    {
        return new Dictionary<string, string>(_bindings);
    }

    public void SetBinding(string command, string gesture)
    {
        _bindings[command] = gesture;
        SaveBindings();
    }

    public void ResetToDefaults()
    {
        _bindings = new Dictionary<string, string>(DefaultBindings);
        SaveBindings();
    }

    private Dictionary<string, string> LoadBindings()
    {
        try
        {
            if (File.Exists(_bindingsPath))
            {
                var json = File.ReadAllText(_bindingsPath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (loaded != null)
                {
                    // Merge with defaults so new commands get their default binding
                    var merged = new Dictionary<string, string>(DefaultBindings);
                    foreach (var kvp in loaded)
                        merged[kvp.Key] = kvp.Value;
                    return merged;
                }
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to load keybindings: {0}", ex.Message);
        }
        return new Dictionary<string, string>(DefaultBindings);
    }

    private void SaveBindings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_bindings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_bindingsPath, json);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to save keybindings: {0}", ex.Message);
        }
    }
}
