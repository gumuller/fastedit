using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private readonly string _tempDir;
    private AppSettings _settings;

    public SettingsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit");

        Directory.CreateDirectory(appDataPath);
        _settingsPath = Path.Combine(appDataPath, "settings.json");

        _tempDir = Path.Combine(appDataPath, "Temp");
        Directory.CreateDirectory(_tempDir);

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
            _settings.AutoSaveIntervalSeconds = value;
            Save();
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
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
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
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to save settings: {0}", ex.Message);
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
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public double WindowWidth { get; set; } = 1100;
        public double WindowHeight { get; set; } = 700;
        public bool WindowMaximized { get; set; }
    }
}
