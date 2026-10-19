using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Xunit.Abstractions;

namespace FastEdit.UiTests;

internal sealed class DesktopApp : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly UIA3Automation _automation;
    private readonly ConcurrentQueue<string> _log = new();
    private readonly string _executable;
    private readonly string _profile;
    private readonly string _diagnostics;
    private readonly string _session;
    private Process? _process;
    private Window? _window;

    public string WorkingDirectory { get; }
    public string ExecutablePath => _executable;
    public string AutoSaveDirectory => Path.Combine(_profile, "AutoSave");

    public DesktopApp(ITestOutputHelper output)
    {
        _output = output;
        if (Environment.GetEnvironmentVariable("FASTEDIT_UI_TESTS") != "1")
            throw new InvalidOperationException(
                "Desktop tests require FASTEDIT_UI_TESTS=1 and a dedicated, unlocked Windows account.");
        using var currentProcess = Process.GetCurrentProcess();
        if (!Environment.UserInteractive || currentProcess.SessionId == 0)
            throw new InvalidOperationException("Desktop tests cannot run in a non-interactive Windows service session.");

        _executable = Path.GetFullPath(
            Environment.GetEnvironmentVariable("FASTEDIT_UI_APP") ??
            throw new InvalidOperationException("Set FASTEDIT_UI_APP to the published FastEdit.exe."));
        if (!File.Exists(_executable))
            throw new FileNotFoundException("The published application was not found.", _executable);

        _profile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FastEdit");
        if (Directory.Exists(_profile) || File.Exists(_profile))
            throw new InvalidOperationException(
                $"Refusing to modify the existing FastEdit profile at '{_profile}'. Use a fresh Windows account.");

        foreach (var process in Process.GetProcessesByName("FastEdit"))
        {
            using (process)
                if (!process.HasExited)
                    throw new InvalidOperationException("Close existing FastEdit processes before running desktop tests.");
        }

        _session = Guid.NewGuid().ToString("N");
        WorkingDirectory = Path.Combine(Path.GetTempPath(), "FastEdit.UiTests", _session);
        _diagnostics = Path.Combine(
            Environment.GetEnvironmentVariable("FASTEDIT_UI_ARTIFACTS") ??
            Path.Combine(Environment.CurrentDirectory, "TestResults", "desktop"),
            _session);
        _automation = new UIA3Automation();
        Directory.CreateDirectory(WorkingDirectory);
        Directory.CreateDirectory(_diagnostics);
        Directory.CreateDirectory(_profile);
        File.WriteAllText(Path.Combine(_profile, ".ui-tests-owner"), _session);
        File.WriteAllText(Path.Combine(_profile, "settings.json"), JsonSerializer.Serialize(new
        {
            CheckForUpdatesOnStartup = false,
            AutoSaveIntervalSeconds = 1,
            LastOpenedFolder = WorkingDirectory,
            WindowLeft = 10,
            WindowTop = 10,
            WindowWidth = 1000,
            WindowHeight = 700
        }));
        _output.WriteLine($"Desktop session artifacts: {_diagnostics}");
    }

    public string CreateFile(string name, string content)
    {
        var path = Path.Combine(WorkingDirectory, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    public void Start(params string[] files) => Start(recover: false, files);

    public void StartWithRecovery() => Start(recover: true, Array.Empty<string>());

    private void Start(bool recover, string[] files)
    {
        if (_process != null)
            throw new InvalidOperationException("The previous application process has not been stopped.");

        var startInfo = new ProcessStartInfo(_executable)
        {
            WorkingDirectory = Path.GetDirectoryName(_executable)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var file in files)
            startInfo.ArgumentList.Add(file);

        _process = new Process { StartInfo = startInfo };
        _process.OutputDataReceived += (_, e) => { if (e.Data != null) _log.Enqueue(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data != null) _log.Enqueue($"stderr: {e.Data}"); };
        if (!_process.Start())
            throw new InvalidOperationException("FastEdit did not start.");
        _log.Enqueue($"Started process {_process.Id}: {string.Join(" ", files)}");
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        if (recover)
            AcceptCrashRecovery();

        WaitForMainWindow();
    }

    public void StartFromExplorerVerb(string file)
    {
        if (_process != null)
            throw new InvalidOperationException("The previous application process has not been stopped.");
        _process = Process.Start(new ProcessStartInfo(file)
        {
            UseShellExecute = true,
            Verb = "FastEdit"
        }) ?? throw new InvalidOperationException("The Explorer verb did not start FastEdit.");
        _log.Enqueue($"Explorer verb started process {_process.Id}: {file}");
        WaitForMainWindow();
    }

    private void WaitForMainWindow()
    {
        Until(() =>
        {
            EnsureRunning();
            _window = _automation.GetDesktop()
                .FindFirstChild(cf => cf.ByProcessId(_process!.Id).And(cf.ByAutomationId("FastEditWindow")))
                ?.AsWindow();
            return _window != null;
        }, "The FastEdit main window did not appear.");
    }

    public void SetExplorerIntegration(bool enabled)
    {
        Find("FileMenu").AsMenuItem().Expand();
        Find("OpenSettings").AsMenuItem().Invoke();
        Find("IntegrationTab").Patterns.SelectionItem.Pattern.Select();
        var checkbox = Find("ExplorerIntegrationCheck").AsCheckBox();
        Assert.True(checkbox.IsEnabled, "Explorer integration is unavailable in Settings.");
        checkbox.IsChecked = enabled;
        Find("SaveSettings").AsButton().Invoke();
        Until(() => _window!.FindFirstDescendant(cf => cf.ByAutomationId("SettingsWindow")) == null,
            "Settings did not close after applying Explorer integration.");
    }

    public AutomationElement Find(string automationId, bool requireVisible = true)
    {
        AutomationElement? element = null;
        Until(() =>
        {
            EnsureRunning();
            element = _window!.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            return element != null && (!requireVisible || !element.IsOffscreen);
        }, $"Visible UI element '{automationId}' was not found.");
        return element!;
    }

    public void WaitForEditor(string fileName)
    {
        Until(() => _window!.FindFirstDescendant(cf =>
                cf.ByAutomationId("TabTitle").And(cf.ByName(fileName))) != null,
            $"The tab '{fileName}' was not opened.");
        Find("TextEditor");
    }

    public void FocusEditor()
    {
        EnsureRunning();
        _window!.Focus();
        Until(() => GetForegroundWindow() == _window.Properties.NativeWindowHandle.Value,
            "Another window is blocking the test application; keyboard and mouse input will not be sent.");
        Find("TextEditor").Click();
    }

    public void ReplaceDocument(string text)
    {
        FocusEditor();
        Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(text);
    }

    public void Save()
    {
        FocusEditor();
        Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
    }

    public static void Shortcut(params VirtualKeyShort[] keys) => Keyboard.TypeSimultaneously(keys);

    public void WaitForName(string automationId, string expected)
    {
        Until(() => Find(automationId).Name == expected,
            $"'{automationId}' did not change to '{expected}'.");
    }

    public MenuItem OpenThemeMenu()
    {
        Find("ViewMenu").AsMenuItem().Expand();
        var menu = Find("ThemeMenu", requireVisible: false).AsMenuItem();
        Until(() =>
        {
            if (!menu.IsOffscreen)
                return true;
            var scrollDown = _window!.Popup?.FindFirstDescendant(cf => cf.ByAutomationId("PART_ButtonScrollDown"));
            Assert.NotNull(scrollDown);
            scrollDown.AsButton().Invoke();
            return false;
        }, "The Theme submenu could not be scrolled into view.");
        menu.Expand();
        return menu;
    }

    public void SelectTheme(string name)
    {
        OpenThemeMenu();
        Find($"Theme.{name}").AsMenuItem().Invoke();
        WaitForName("ThemeStatus", name);
    }

    private void AcceptCrashRecovery()
    {
        AutomationElement? dialog = null;
        Until(() =>
        {
            dialog = _automation.GetDesktop().FindFirstDescendant(cf =>
                cf.ByProcessId(_process!.Id).And(cf.ByControlType(ControlType.Window)).And(cf.ByName("Crash Recovery")));
            return dialog != null;
        }, "The crash recovery confirmation did not appear.");
        var yes = dialog!.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Yes")));
        Assert.NotNull(yes);
        yes.AsButton().Invoke();
    }

    public void Close()
    {
        EnsureRunning();
        _window!.Close();
        Assert.True(_process!.WaitForExit(30_000), "FastEdit did not shut down without a prompt.");
        _process.WaitForExit();
        Assert.Equal(0, _process.ExitCode);
        _log.Enqueue($"Process {_process.Id} exited cleanly.");
        _process.Dispose();
        _process = null;
        _window = null;
    }

    public void Crash()
    {
        EnsureRunning();
        _log.Enqueue($"Simulating a crash of process {_process!.Id}.");
        _process.Kill(entireProcessTree: true);
        Assert.True(_process.WaitForExit(30_000), "The test process did not terminate.");
        _process.WaitForExit();
        _process.Dispose();
        _process = null;
        _window = null;
    }

    public void Snapshot(string name)
    {
        using var image = Capture.Screen();
        image.ToFile(Path.Combine(_diagnostics, $"{name}.png"));
    }

    public static void Until(Func<bool> condition, string failureMessage)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (condition())
                return;
            Thread.Sleep(100);
        }
        Assert.Fail(failureMessage);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();

    private void EnsureRunning()
    {
        if (_process == null || _process.HasExited)
            throw new InvalidOperationException(
                $"FastEdit exited unexpectedly. Exit code: {_process?.ExitCode}. {string.Join(Environment.NewLine, _log)}");
    }

    private void CaptureDiagnostics()
    {
        Snapshot("desktop");
        if (_process == null || _process.HasExited)
            return;

        var tree = new StringBuilder();
        var remaining = 500;
        try
        {
            foreach (var window in _automation.GetDesktop().FindAllChildren(cf => cf.ByProcessId(_process.Id)))
                Dump(window, 0);
        }
        finally
        {
            File.WriteAllText(Path.Combine(_diagnostics, "ui-tree.txt"), tree.ToString());
        }

        void Dump(AutomationElement element, int depth)
        {
            if (remaining-- <= 0 || depth > 12)
                return;
            var id = element.Properties.AutomationId.TryGetValue(out var automationId) ? automationId : "<unsupported>";
            tree.Append(' ', depth * 2).AppendLine(
                $"{element.ControlType} id={id} name={element.Name} enabled={element.IsEnabled} offscreen={element.IsOffscreen}");
            foreach (var child in element.FindAllChildren())
                Dump(child, depth + 1);
        }
    }

    public void Dispose()
    {
        try
        {
            CaptureDiagnostics();
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Could not capture desktop diagnostics: {ex}");
            File.WriteAllText(Path.Combine(_diagnostics, "diagnostic-error.txt"), ex.ToString());
        }
        finally
        {
            if (_process != null)
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    if (!_process.WaitForExit(30_000))
                        throw new TimeoutException($"Test process {_process.Id} could not be stopped.");
                    _process.WaitForExit();
                }
                _process.Dispose();
            }
            _automation.Dispose();
            File.WriteAllLines(Path.Combine(_diagnostics, "application.log"), _log);
            if (File.ReadAllText(Path.Combine(_profile, ".ui-tests-owner")) != _session)
                throw new InvalidOperationException("Refusing to clean a profile not owned by this desktop test.");
            foreach (var file in Directory.GetFiles(_profile, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(_diagnostics, "profile", Path.GetRelativePath(_profile, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            Directory.Delete(_profile, recursive: true);
            Directory.Delete(WorkingDirectory, recursive: true);
        }
    }
}
