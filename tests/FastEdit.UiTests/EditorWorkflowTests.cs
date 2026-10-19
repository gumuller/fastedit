using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FastEdit.UiTests;

public sealed class EditorWorkflowTests
{
    private readonly ITestOutputHelper _output;

    public EditorWorkflowTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [Trait("Category", "Smoke")]
    public void Open_Edit_And_Save_Updates_The_Actual_File()
    {
        using var app = new DesktopApp(_output);
        var path = app.CreateFile("edit and save.txt", "original\r\n");
        app.Start(path);
        app.WaitForEditor(Path.GetFileName(path));
        app.FocusEditor();
        DesktopApp.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.END);
        Keyboard.Type("saved through the UI");
        app.Save();
        DesktopApp.Until(() => File.ReadAllText(path) == "original\r\nsaved through the UI",
            "The edited document was not saved to disk.");
        app.Close();
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public void Replace_All_Changes_And_Saves_Every_Match()
    {
        using var app = new DesktopApp(_output);
        var path = app.CreateFile("replace.txt", "alpha middle alpha");
        app.Start(path);
        app.WaitForEditor(Path.GetFileName(path));
        app.FocusEditor();
        DesktopApp.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_H);
        app.Find("PART_searchTextBox").AsTextBox().Text = "alpha";
        app.Find("PART_replaceTextBox").AsTextBox().Text = "beta";
        app.Find("PART_replaceAllButton").AsButton().Invoke();
        app.WaitForName("StatusText", "Replaced 2 occurrence(s)");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        app.Save();
        DesktopApp.Until(() => File.ReadAllText(path) == "beta middle beta",
            "Replace All did not persist both replacements.");
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public void Text_Hex_Roundtrip_Preserves_UTF8_Bytes()
    {
        using var app = new DesktopApp(_output);
        var path = app.CreateFile("roundtrip.txt", "Tokyo \u6771\u4eac\r\n");
        var original = File.ReadAllBytes(path);
        app.Start(path);
        app.WaitForEditor(Path.GetFileName(path));
        app.Find("EditorMode").AsButton().Invoke();
        app.WaitForName("EditorMode", "Hex");
        app.Find("HexEditor");
        app.Find("EditorMode").AsButton().Invoke();
        app.WaitForName("EditorMode", "Text");
        app.FocusEditor();
        DesktopApp.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.END);
        Keyboard.Type("roundtrip");
        app.Save();
        var expected = original.Concat(System.Text.Encoding.UTF8.GetBytes("roundtrip")).ToArray();
        DesktopApp.Until(() => File.ReadAllBytes(path).SequenceEqual(expected),
            "Text/hex switching changed the original UTF-8 bytes or prevented subsequent editing.");
    }

    [Fact]
    [Trait("Category", "E2E")]
    public void Every_Built_In_Theme_Is_Selectable_And_Persists_After_Restart()
    {
        using var app = new DesktopApp(_output);
        var path = app.CreateFile("themes.cs", "public class Tokyo { /* theme preview */ }\r\n");
        app.Start(path);
        app.WaitForEditor(Path.GetFileName(path));
        var themes = app.OpenThemeMenu()
            .FindAllChildren(cf => cf.ByControlType(ControlType.MenuItem))
            .Select(item => item.AutomationId)
            .ToArray();
        Assert.NotEmpty(themes);
        Assert.Contains("Theme.TokyoNight", themes);
        Assert.Contains("Theme.TokyoNightStorm", themes);
        Assert.Contains("Theme.TokyoNightDay", themes);
        foreach (var theme in themes)
        {
            Assert.StartsWith("Theme.", theme);
            var name = theme["Theme.".Length..];
            app.SelectTheme(name);
            app.Snapshot(name);
        }
        app.Close();
        app.Start();
        app.WaitForEditor(Path.GetFileName(path));
        app.WaitForName("ThemeStatus", themes[^1]["Theme.".Length..]);
    }

    [Fact]
    [Trait("Category", "E2E")]
    public void Closing_And_Restarting_Restores_Tabs_And_Unsaved_Edits()
    {
        using var app = new DesktopApp(_output);
        var first = app.CreateFile("first.txt", "first document");
        var second = app.CreateFile("second.txt", "original second document");
        app.Start(first, second);
        app.WaitForEditor(Path.GetFileName(second));
        app.ReplaceDocument("unsaved text restored after shutdown");
        app.Close();
        Assert.Equal("original second document", File.ReadAllText(second));

        app.Start();
        app.WaitForEditor(Path.GetFileName(first));
        app.WaitForEditor(Path.GetFileName(second));
        app.Save();
        DesktopApp.Until(() => File.ReadAllText(second) == "unsaved text restored after shutdown",
            "The restarted application did not restore and save the unsaved document.");
    }

    [Fact]
    [Trait("Category", "E2E")]
    public void Crash_Recovery_Restores_The_Last_AutoSave()
    {
        using var app = new DesktopApp(_output);
        var path = app.CreateFile("crash.txt", "original on disk");
        const string recovered = "unsaved content recovered after a process crash";
        app.Start(path);
        app.WaitForEditor(Path.GetFileName(path));
        app.ReplaceDocument(recovered);
        DesktopApp.Until(() => HasPublishedAutoSave(app.AutoSaveDirectory, path, recovered.Length),
            "A recoverable autosave was not published before the crash.");
        app.Crash();
        Assert.Equal("original on disk", File.ReadAllText(path));

        app.Start();
        app.AcceptCrashRecovery();
        app.WaitForEditor(Path.GetFileName(path));
        app.Save();
        DesktopApp.Until(() => File.ReadAllText(path) == recovered,
            "Crash recovery did not restore and save the autosaved content.");
    }

    private static bool HasPublishedAutoSave(string directory, string filePath, int expectedCursorOffset)
    {
        if (!Directory.Exists(directory))
            return false;
        try
        {
            foreach (var manifestPath in Directory.GetFiles(directory, "manifest-*.json"))
            {
                using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
                foreach (var entry in manifest.RootElement.EnumerateArray())
                {
                    if (string.Equals(entry.GetProperty("FilePath").GetString(), filePath, StringComparison.OrdinalIgnoreCase) &&
                        entry.GetProperty("IsModified").GetBoolean() &&
                        entry.GetProperty("CursorOffset").GetInt32() == expectedCursorOffset)
                    {
                        var contentFile = entry.GetProperty("ContentFile").GetString() ??
                            throw new InvalidDataException("The autosave manifest has no content file.");
                        if (File.Exists(Path.Combine(directory, contentFile)))
                            return true;
                    }
                }
            }
        }
        catch (FileNotFoundException)
        {
            // Autosave replaces its previous generation while this condition is polled.
        }
        return false;
    }
}
