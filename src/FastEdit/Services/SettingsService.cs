using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private readonly IFileSystemService _fileSystem;
    private readonly string _settingsPath;
    private readonly string _settingsLockName;
    private AppSettings _settings;

    public event EventHandler? AutoSaveIntervalChanged;

    public SettingsService(IFileSystemService fileSystem)
        : this(
            fileSystem,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FastEdit"))
    {
    }

    internal SettingsService(IFileSystemService fileSystem, string appDataPath)
    {
        _fileSystem = fileSystem;

        _fileSystem.CreateDirectory(appDataPath);
        _settingsPath = Path.Combine(appDataPath, "settings.json");

        var normalizedSettingsPath = Path.GetFullPath(_settingsPath).ToUpperInvariant();
        var settingsPathHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedSettingsPath)));
        _settingsLockName = $@"Local\FastEdit.Settings.{settingsPathHash}";

        _settings = LoadSettings();
    }

    public string ThemeName
    {
        get => _settings.ThemeName;
        set
        {
            _settings.ThemeName = value;
            Save();
        }
    }

    public string? LastOpenedFolder
    {
        get => _settings.LastOpenedFolder;
        set
        {
            _settings.LastOpenedFolder = value;
            Save();
        }
    }

    public List<SessionFile> OpenFiles
    {
        get => _settings.OpenFiles;
        set => _settings.OpenFiles = value;
    }

    public int ActiveTabIndex
    {
        get => _settings.ActiveTabIndex;
        set => _settings.ActiveTabIndex = value;
    }

    public string? ActiveSessionEntryId
    {
        get => _settings.ActiveSessionEntryId;
        set => _settings.ActiveSessionEntryId = value;
    }

    public List<string> RecentFiles
    {
        get => _settings.RecentFiles;
        set => _settings.RecentFiles = value;
    }

    public bool WordWrapEnabled
    {
        get => _settings.WordWrapEnabled;
        set
        {
            _settings.WordWrapEnabled = value;
            Save();
        }
    }

    public bool ShowWhitespace
    {
        get => _settings.ShowWhitespace;
        set
        {
            _settings.ShowWhitespace = value;
            Save();
        }
    }

    public double EditorFontSize
    {
        get => _settings.EditorFontSize;
        set
        {
            _settings.EditorFontSize = Math.Clamp(value, 8, 72);
            Save();
        }
    }

    public double WindowLeft
    {
        get => _settings.WindowLeft;
        set => _settings.WindowLeft = value;
    }

    public double WindowTop
    {
        get => _settings.WindowTop;
        set => _settings.WindowTop = value;
    }

    public double WindowWidth
    {
        get => _settings.WindowWidth;
        set => _settings.WindowWidth = value;
    }

    public double WindowHeight
    {
        get => _settings.WindowHeight;
        set => _settings.WindowHeight = value;
    }

    public bool WindowMaximized
    {
        get => _settings.WindowMaximized;
        set => _settings.WindowMaximized = value;
    }

    public bool CheckForUpdatesOnStartup
    {
        get => _settings.CheckForUpdatesOnStartup;
        set
        {
            _settings.CheckForUpdatesOnStartup = value;
            Save();
        }
    }

    public int AutoSaveIntervalSeconds
    {
        get => _settings.AutoSaveIntervalSeconds;
        set
        {
            var normalizedValue = Math.Max(1, value);
            if (_settings.AutoSaveIntervalSeconds == normalizedValue)
                return;

            var previousValue = _settings.AutoSaveIntervalSeconds;
            _settings.AutoSaveIntervalSeconds = normalizedValue;
            try
            {
                Save();
            }
            catch
            {
                _settings.AutoSaveIntervalSeconds = previousValue;
                throw;
            }
            AutoSaveIntervalChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int TabSize
    {
        get => _settings.TabSize;
        set
        {
            _settings.TabSize = value;
            Save();
        }
    }

    public bool UseTabs
    {
        get => _settings.UseTabs;
        set
        {
            _settings.UseTabs = value;
            Save();
        }
    }

    public string CursorStyle
    {
        get => _settings.CursorStyle;
        set
        {
            _settings.CursorStyle = value;
            Save();
        }
    }

    public void AddRecentFile(string filePath)
    {
        _settings.RecentFiles.Remove(filePath);
        _settings.RecentFiles.Insert(0, filePath);
        if (_settings.RecentFiles.Count > 10)
            _settings.RecentFiles.RemoveRange(10, _settings.RecentFiles.Count - 10);
        Save();
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (_fileSystem.FileExists(_settingsPath))
            {
                var json = _fileSystem.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to load settings: {0}", ex.Message);
        }
        return new AppSettings();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_settings, SerializerOptions);
        using var publicationLock = new Mutex(false, _settingsLockName);
        var lockTaken = false;
        try
        {
            try
            {
                lockTaken = publicationLock.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                lockTaken = true;
            }

            if (!lockTaken)
                throw new IOException("Timed out waiting to publish FastEdit settings.");

            _fileSystem.WriteAllTextAtomic(_settingsPath, json);
        }
        finally
        {
            if (lockTaken)
                publicationLock.ReleaseMutex();
        }
    }

    private class AppSettings
    {
        public string ThemeName { get; set; } = "Dark";
        public string? LastOpenedFolder { get; set; }
        public List<SessionFile> OpenFiles { get; set; } = new();
        public int ActiveTabIndex { get; set; }
        public string? ActiveSessionEntryId { get; set; }
        public List<string> RecentFiles { get; set; } = new();
        public bool WordWrapEnabled { get; set; }
        public bool ShowWhitespace { get; set; }
        public double EditorFontSize { get; set; } = 14;
        public bool CheckForUpdatesOnStartup { get; set; } = true;
        public int AutoSaveIntervalSeconds { get; set; } = 30;
        public int TabSize { get; set; } = 4;
        public bool UseTabs { get; set; }
        public string CursorStyle { get; set; } = "Line";
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public double WindowWidth { get; set; } = 1100;
        public double WindowHeight { get; set; } = 700;
        public bool WindowMaximized { get; set; }
    }
}
