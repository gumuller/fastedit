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
    private SettingsDirtyFields _dirtyFields;
    private long _loadedSessionGeneration;
    private bool _sessionWriteConflicted;

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
        _loadedSessionGeneration = _settings.SessionGeneration;
    }

    public string ThemeName
    {
        get => _settings.ThemeName;
        set
        {
            _settings.ThemeName = value;
            _dirtyFields |= SettingsDirtyFields.ThemeName;
            Save();
        }
    }

    public string? LastOpenedFolder
    {
        get => _settings.LastOpenedFolder;
        set
        {
            _settings.LastOpenedFolder = value;
            _dirtyFields |= SettingsDirtyFields.LastOpenedFolder;
            Save();
        }
    }

    public List<SessionFile> OpenFiles
    {
        get => _settings.OpenFiles;
        set
        {
            _settings.OpenFiles = value;
            MarkSessionDirty();
        }
    }

    public int ActiveTabIndex
    {
        get => _settings.ActiveTabIndex;
        set
        {
            _settings.ActiveTabIndex = value;
            MarkSessionDirty();
        }
    }

    public string? ActiveSessionEntryId
    {
        get => _settings.ActiveSessionEntryId;
        set
        {
            _settings.ActiveSessionEntryId = value;
            MarkSessionDirty();
        }
    }

    public List<string> RecentFiles
    {
        get => _settings.RecentFiles;
        set
        {
            _settings.RecentFiles = value;
            _dirtyFields |= SettingsDirtyFields.RecentFiles;
        }
    }

    public bool WordWrapEnabled
    {
        get => _settings.WordWrapEnabled;
        set
        {
            _settings.WordWrapEnabled = value;
            _dirtyFields |= SettingsDirtyFields.WordWrapEnabled;
            Save();
        }
    }

    public bool ShowWhitespace
    {
        get => _settings.ShowWhitespace;
        set
        {
            _settings.ShowWhitespace = value;
            _dirtyFields |= SettingsDirtyFields.ShowWhitespace;
            Save();
        }
    }

    public double EditorFontSize
    {
        get => _settings.EditorFontSize;
        set
        {
            _settings.EditorFontSize = Math.Clamp(value, 8, 72);
            _dirtyFields |= SettingsDirtyFields.EditorFontSize;
            Save();
        }
    }

    public double WindowLeft
    {
        get => _settings.WindowLeft;
        set
        {
            _settings.WindowLeft = value;
            _dirtyFields |= SettingsDirtyFields.WindowPlacement;
        }
    }

    public double WindowTop
    {
        get => _settings.WindowTop;
        set
        {
            _settings.WindowTop = value;
            _dirtyFields |= SettingsDirtyFields.WindowPlacement;
        }
    }

    public double WindowWidth
    {
        get => _settings.WindowWidth;
        set
        {
            _settings.WindowWidth = value;
            _dirtyFields |= SettingsDirtyFields.WindowPlacement;
        }
    }

    public double WindowHeight
    {
        get => _settings.WindowHeight;
        set
        {
            _settings.WindowHeight = value;
            _dirtyFields |= SettingsDirtyFields.WindowPlacement;
        }
    }

    public bool WindowMaximized
    {
        get => _settings.WindowMaximized;
        set
        {
            _settings.WindowMaximized = value;
            _dirtyFields |= SettingsDirtyFields.WindowPlacement;
        }
    }

    public bool CheckForUpdatesOnStartup
    {
        get => _settings.CheckForUpdatesOnStartup;
        set
        {
            _settings.CheckForUpdatesOnStartup = value;
            _dirtyFields |= SettingsDirtyFields.CheckForUpdates;
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
            _dirtyFields |= SettingsDirtyFields.AutoSaveInterval;
            try
            {
                Save();
            }
            catch
            {
                _settings.AutoSaveIntervalSeconds = previousValue;
                _dirtyFields &= ~SettingsDirtyFields.AutoSaveInterval;
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
            _dirtyFields |= SettingsDirtyFields.TabSize;
            Save();
        }
    }

    public bool UseTabs
    {
        get => _settings.UseTabs;
        set
        {
            _settings.UseTabs = value;
            _dirtyFields |= SettingsDirtyFields.UseTabs;
            Save();
        }
    }

    public string CursorStyle
    {
        get => _settings.CursorStyle;
        set
        {
            _settings.CursorStyle = value;
            _dirtyFields |= SettingsDirtyFields.CursorStyle;
            Save();
        }
    }

    public void AddRecentFile(string filePath)
    {
        _settings.RecentFiles.Remove(filePath);
        _settings.RecentFiles.Insert(0, filePath);
        if (_settings.RecentFiles.Count > 10)
            _settings.RecentFiles.RemoveRange(10, _settings.RecentFiles.Count - 10);
        _dirtyFields |= SettingsDirtyFields.RecentFiles;
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
        var dirtyFields = _dirtyFields;
        if (_sessionWriteConflicted)
            dirtyFields &= ~SettingsDirtyFields.Session;
        if (dirtyFields == SettingsDirtyFields.None)
            return;

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

            var latest = LoadSettingsForMerge();
            if ((dirtyFields & SettingsDirtyFields.Session) != 0 &&
                latest.SessionGeneration != _loadedSessionGeneration)
            {
                _loadedSessionGeneration = latest.SessionGeneration;
                _sessionWriteConflicted = true;
                throw new IOException(
                    "The shutdown session changed in another FastEdit instance. " +
                    "Close again to replace that newer session.");
            }

            MergeDirtySettings(latest, _settings, dirtyFields);
            if ((dirtyFields & SettingsDirtyFields.Session) != 0)
                latest.SessionGeneration++;
            var json = JsonSerializer.Serialize(latest, SerializerOptions);
            _fileSystem.WriteAllTextAtomic(_settingsPath, json);
            _settings = latest;
            _loadedSessionGeneration = latest.SessionGeneration;
            _dirtyFields &= ~dirtyFields;
            if (_sessionWriteConflicted)
            {
                _dirtyFields &= ~SettingsDirtyFields.Session;
                _sessionWriteConflicted = false;
            }
        }
        finally
        {
            if (lockTaken)
                publicationLock.ReleaseMutex();
        }
    }

    private void MarkSessionDirty()
    {
        _dirtyFields |= SettingsDirtyFields.Session;
        _sessionWriteConflicted = false;
    }

    private AppSettings LoadSettingsForMerge()
    {
        if (!_fileSystem.FileExists(_settingsPath))
            return new AppSettings();

        var json = _fileSystem.ReadAllText(_settingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ??
            throw new InvalidDataException("FastEdit settings could not be deserialized.");
    }

    private static void MergeDirtySettings(
        AppSettings target,
        AppSettings source,
        SettingsDirtyFields dirtyFields)
    {
        if ((dirtyFields & SettingsDirtyFields.ThemeName) != 0)
            target.ThemeName = source.ThemeName;
        if ((dirtyFields & SettingsDirtyFields.LastOpenedFolder) != 0)
            target.LastOpenedFolder = source.LastOpenedFolder;
        if ((dirtyFields & SettingsDirtyFields.Session) != 0)
        {
            target.OpenFiles = source.OpenFiles;
            target.ActiveTabIndex = source.ActiveTabIndex;
            target.ActiveSessionEntryId = source.ActiveSessionEntryId;
        }
        if ((dirtyFields & SettingsDirtyFields.RecentFiles) != 0)
            target.RecentFiles = source.RecentFiles;
        if ((dirtyFields & SettingsDirtyFields.WordWrapEnabled) != 0)
            target.WordWrapEnabled = source.WordWrapEnabled;
        if ((dirtyFields & SettingsDirtyFields.ShowWhitespace) != 0)
            target.ShowWhitespace = source.ShowWhitespace;
        if ((dirtyFields & SettingsDirtyFields.EditorFontSize) != 0)
            target.EditorFontSize = source.EditorFontSize;
        if ((dirtyFields & SettingsDirtyFields.CheckForUpdates) != 0)
            target.CheckForUpdatesOnStartup = source.CheckForUpdatesOnStartup;
        if ((dirtyFields & SettingsDirtyFields.AutoSaveInterval) != 0)
            target.AutoSaveIntervalSeconds = source.AutoSaveIntervalSeconds;
        if ((dirtyFields & SettingsDirtyFields.TabSize) != 0)
            target.TabSize = source.TabSize;
        if ((dirtyFields & SettingsDirtyFields.UseTabs) != 0)
            target.UseTabs = source.UseTabs;
        if ((dirtyFields & SettingsDirtyFields.CursorStyle) != 0)
            target.CursorStyle = source.CursorStyle;
        if ((dirtyFields & SettingsDirtyFields.WindowPlacement) != 0)
        {
            target.WindowLeft = source.WindowLeft;
            target.WindowTop = source.WindowTop;
            target.WindowWidth = source.WindowWidth;
            target.WindowHeight = source.WindowHeight;
            target.WindowMaximized = source.WindowMaximized;
        }
    }

    [Flags]
    private enum SettingsDirtyFields
    {
        None = 0,
        ThemeName = 1 << 0,
        LastOpenedFolder = 1 << 1,
        Session = 1 << 2,
        RecentFiles = 1 << 3,
        WordWrapEnabled = 1 << 4,
        ShowWhitespace = 1 << 5,
        EditorFontSize = 1 << 6,
        CheckForUpdates = 1 << 7,
        AutoSaveInterval = 1 << 8,
        TabSize = 1 << 9,
        UseTabs = 1 << 10,
        CursorStyle = 1 << 11,
        WindowPlacement = 1 << 12
    }

    private class AppSettings
    {
        public string ThemeName { get; set; } = "Dark";
        public string? LastOpenedFolder { get; set; }
        public List<SessionFile> OpenFiles { get; set; } = new();
        public int ActiveTabIndex { get; set; }
        public string? ActiveSessionEntryId { get; set; }
        public long SessionGeneration { get; set; }
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
