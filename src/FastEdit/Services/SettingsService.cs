using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public class SettingsService : ISettingsService, IShutdownSessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private readonly IFileSystemService _fileSystem;
    private readonly string _settingsPath;
    private readonly string _tempDir;
    private readonly string _settingsMutexName;
    private AppSettings _settings;
    private PendingNonSessionSettings _pendingNonSessionSettings;

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
        _settingsMutexName = CreateSettingsMutexName(_settingsPath);

        _tempDir = Path.Combine(appDataPath, "Temp");
        _fileSystem.CreateDirectory(_tempDir);

        _settings = LoadSettings();
    }

    public string TempDirectory => _tempDir;

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

    public List<string> RecentFiles
    {
        get => _settings.RecentFiles;
        set
        {
            _settings.RecentFiles = value;
            _pendingNonSessionSettings |= PendingNonSessionSettings.RecentFiles;
        }
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
        set
        {
            _settings.WindowLeft = value;
            _pendingNonSessionSettings |= PendingNonSessionSettings.WindowLeft;
        }
    }

    public double WindowTop
    {
        get => _settings.WindowTop;
        set
        {
            _settings.WindowTop = value;
            _pendingNonSessionSettings |= PendingNonSessionSettings.WindowTop;
        }
    }

    public double WindowWidth
    {
        get => _settings.WindowWidth;
        set
        {
            _settings.WindowWidth = value;
            _pendingNonSessionSettings |= PendingNonSessionSettings.WindowWidth;
        }
    }

    public double WindowHeight
    {
        get => _settings.WindowHeight;
        set
        {
            _settings.WindowHeight = value;
            _pendingNonSessionSettings |= PendingNonSessionSettings.WindowHeight;
        }
    }

    public bool WindowMaximized
    {
        get => _settings.WindowMaximized;
        set
        {
            _settings.WindowMaximized = value;
            _pendingNonSessionSettings |= PendingNonSessionSettings.WindowMaximized;
        }
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
        WithSettingsLock(() =>
        {
            if (_fileSystem.FileExists(_settingsPath))
            {
                var latest = LoadSettingsStrict();
                _settings.OpenFiles = latest.OpenFiles;
                _settings.ActiveTabIndex = latest.ActiveTabIndex;
            }

            SaveSettings(_settings);
            _pendingNonSessionSettings = PendingNonSessionSettings.None;
        });
    }

    public ShutdownSessionState ReadShutdownSession(
        Action<ShutdownSessionState>? whileLocked = null) =>
        WithSettingsLock(() =>
        {
            var latest = LoadSettingsStrict();
            _settings.OpenFiles = latest.OpenFiles;
            _settings.ActiveTabIndex = latest.ActiveTabIndex;
            var session = CreateShutdownSessionState(latest);
            whileLocked?.Invoke(session);
            return session;
        });

    public ShutdownSessionPublication PublishShutdownSession(
        ShutdownSessionState session,
        Action<ShutdownSessionPublication>? whileLocked = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        return WithSettingsLock(() =>
        {
            var latest = LoadSettingsStrict();
            var previous = CreateShutdownSessionState(latest);
            MergePendingNonSessionSettings(latest);
            var replacedOwners = session.ReplacedOwners?
                .Where(owner => !string.IsNullOrWhiteSpace(owner))
                .ToHashSet(StringComparer.Ordinal) ??
                new HashSet<string>(StringComparer.Ordinal);
            var incomingOwners = session.Files
                .Select(file => file.SnapshotOwner)
                .Where(owner => !string.IsNullOrWhiteSpace(owner))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);
            replacedOwners.UnionWith(incomingOwners);
            var replaceLegacyEntries = session.ReplacedOwners != null;
            var retainedFiles = replacedOwners.Count == 0
                ? new List<SessionFile>()
                : latest.OpenFiles
                    .Where(file =>
                        string.IsNullOrWhiteSpace(file.SnapshotOwner)
                            ? !replaceLegacyEntries
                            : !replacedOwners.Contains(file.SnapshotOwner))
                    .Select(CloneSessionFile)
                    .ToList();
            foreach (var retainedFile in retainedFiles)
                retainedFile.IsActive = false;
            var incomingFiles = session.Files.Select(CloneSessionFile).ToList();
            latest.OpenFiles = retainedFiles.Concat(incomingFiles).ToList();
            latest.ActiveTabIndex = incomingFiles.Count == 0
                ? Math.Clamp(
                    latest.ActiveTabIndex,
                    0,
                    Math.Max(0, retainedFiles.Count - 1))
                : retainedFiles.Count + Math.Clamp(
                    session.ActiveTabIndex,
                    0,
                    incomingFiles.Count - 1);
            SaveSettings(latest);
            _settings = latest;
            _pendingNonSessionSettings = PendingNonSessionSettings.None;
            var publication = new ShutdownSessionPublication(
                previous,
                CreateShutdownSessionState(latest));
            whileLocked?.Invoke(publication);
            return publication;
        });
    }

    private AppSettings LoadSettingsStrict()
    {
        if (!_fileSystem.FileExists(_settingsPath))
            return new AppSettings();

        var json = _fileSystem.ReadAllText(_settingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions)
            ?? throw new InvalidDataException("The settings file contains no data.");
    }

    private void SaveSettings(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        _fileSystem.WriteAllTextAtomic(_settingsPath, json);
    }

    private void MergePendingNonSessionSettings(AppSettings latest)
    {
        if (_pendingNonSessionSettings.HasFlag(
                PendingNonSessionSettings.RecentFiles))
        {
            latest.RecentFiles = _settings.RecentFiles.ToList();
        }
        if (_pendingNonSessionSettings.HasFlag(
                PendingNonSessionSettings.WindowLeft))
        {
            latest.WindowLeft = _settings.WindowLeft;
        }
        if (_pendingNonSessionSettings.HasFlag(
                PendingNonSessionSettings.WindowTop))
        {
            latest.WindowTop = _settings.WindowTop;
        }
        if (_pendingNonSessionSettings.HasFlag(
                PendingNonSessionSettings.WindowWidth))
        {
            latest.WindowWidth = _settings.WindowWidth;
        }
        if (_pendingNonSessionSettings.HasFlag(
                PendingNonSessionSettings.WindowHeight))
        {
            latest.WindowHeight = _settings.WindowHeight;
        }
        if (_pendingNonSessionSettings.HasFlag(
                PendingNonSessionSettings.WindowMaximized))
        {
            latest.WindowMaximized = _settings.WindowMaximized;
        }
    }

    private static ShutdownSessionState CreateShutdownSessionState(
        AppSettings settings) =>
        new(
            settings.OpenFiles.Select(CloneSessionFile).ToArray(),
            settings.ActiveTabIndex);

    private static SessionFile CloneSessionFile(SessionFile file) =>
        new()
        {
            FilePath = file.FilePath,
            FileName = file.FileName,
            TabIdentity = file.TabIdentity,
            IsUntitled = file.IsUntitled,
            IsBinaryMode = file.IsBinaryMode,
            Mode = file.Mode,
            IsModified = file.IsModified,
            IsActive = file.IsActive,
            TempFilePath = file.TempFilePath,
            Content = file.Content,
            SnapshotGeneration = file.SnapshotGeneration,
            SnapshotFile = file.SnapshotFile,
            SnapshotFormat = file.SnapshotFormat,
            SnapshotOwner = file.SnapshotOwner,
            SnapshotGenerationFiles =
                file.SnapshotGenerationFiles?.ToList() ?? new List<string>(),
            BaseContentHash = file.BaseContentHash,
            EncodingCodePage = file.EncodingCodePage,
            HasBom = file.HasBom,
            CursorOffset = file.CursorOffset,
            ScrollOffset = file.ScrollOffset,
            HexOffset = file.HexOffset,
            BytesPerRow = file.BytesPerRow,
            LargeFileTopLine = file.LargeFileTopLine
        };

    private static string CreateSettingsMutexName(string settingsPath)
    {
        var normalizedPath = Path.GetFullPath(settingsPath).ToUpperInvariant();
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)));
        return $"Local\\FastEdit-Settings-{hash}";
    }

    private void WithSettingsLock(Action action) =>
        WithSettingsLock(() =>
        {
            action();
            return true;
        });

    private T WithSettingsLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, _settingsMutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
                throw new TimeoutException("Timed out waiting to persist FastEdit settings.");
            return action();
        }
        finally
        {
            if (acquired)
                mutex.ReleaseMutex();
        }
    }

    public string GetTempFilePath(string fileName)
    {
        return Path.Combine(_tempDir, $"{Guid.NewGuid():N}_{fileName}");
    }

    public void CleanupTempFiles()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_tempDir))
            {
                File.Delete(file);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private class AppSettings
    {
        public string ThemeName { get; set; } = "Dark";
        public string? LastOpenedFolder { get; set; }
        public List<SessionFile> OpenFiles { get; set; } = new();
        public int ActiveTabIndex { get; set; }
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

    [Flags]
    private enum PendingNonSessionSettings
    {
        None = 0,
        RecentFiles = 1 << 0,
        WindowLeft = 1 << 1,
        WindowTop = 1 << 2,
        WindowWidth = 1 << 3,
        WindowHeight = 1 << 4,
        WindowMaximized = 1 << 5
    }
}
