using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using FastEdit.Services.Interfaces;
using Microsoft.Win32;

namespace FastEdit.Services;

public sealed class ShellIntegrationService : IShellIntegrationService
{
    private const uint AssociationChanged = 0x08000000;
    private const uint FlushWithoutWaiting = 0x2000;
    private readonly string _executablePath;
    private readonly string _verbPath;
    private readonly Action _notifyChanged;

    public ShellIntegrationService()
        : this(GetExecutablePath(), @"*\shell\FastEdit", NotifyChanged)
    {
    }

    internal ShellIntegrationService(string executablePath, string verbPath, Action notifyChanged)
    {
        if (!Path.IsPathFullyQualified(executablePath))
            throw new ArgumentException("The application path must be absolute.", nameof(executablePath));
        ArgumentException.ThrowIfNullOrWhiteSpace(verbPath);
        ArgumentNullException.ThrowIfNull(notifyChanged);
        _executablePath = Path.GetFullPath(executablePath);
        _verbPath = verbPath;
        _notifyChanged = notifyChanged;
    }

    private string OpenCommand => $"\"{_executablePath}\" \"%1\"";

    public bool IsEnabled
    {
        get
        {
            using var verb = Registry.ClassesRoot.OpenSubKey(_verbPath);
            if (verb == null ||
                verb.GetValue("LegacyDisable") != null ||
                verb.GetValue("ProgrammaticAccessOnly") != null)
                return false;

            using var command = verb.OpenSubKey("command");
            return command?.GetValue(null) is string value &&
                string.Equals(value, OpenCommand, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled && !File.Exists(_executablePath))
            throw new FileNotFoundException(
                "FastEdit.exe was not found. Enable Explorer integration from an installed or published build.",
                _executablePath);
        if (!enabled && !IsEnabled)
            return;

        using (var verb = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + _verbPath) ??
            throw new IOException("The per-user Explorer registration could not be opened for writing."))
        {
            verb.SetValue("LegacyDisable", string.Empty, RegistryValueKind.String);
            verb.SetValue("FastEditExecutable", _executablePath, RegistryValueKind.String);
            if (enabled)
            {
                verb.SetValue(null, "Edit with FastEdit", RegistryValueKind.String);
                verb.SetValue("Icon", $"\"{_executablePath}\",0", RegistryValueKind.String);
                verb.SetValue("MultiSelectModel", "Document", RegistryValueKind.String);
                using (var command = verb.CreateSubKey("command") ??
                    throw new IOException("The Explorer launch command could not be opened for writing."))
                    command.SetValue(null, OpenCommand, RegistryValueKind.String);
                verb.DeleteValue("LegacyDisable", throwOnMissingValue: false);
            }
        }

        if (IsEnabled != enabled)
            throw new InvalidOperationException(
                "Windows did not apply the Explorer integration change. Check registry permissions or machine-wide settings.");
        _notifyChanged();
    }

    private static string GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) &&
            Assembly.GetEntryAssembly() == typeof(ShellIntegrationService).Assembly &&
            !string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            return processPath;
        return Path.Combine(AppContext.BaseDirectory, "FastEdit.exe");
    }

    private static void NotifyChanged() =>
        SHChangeNotify(AssociationChanged, FlushWithoutWaiting, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
