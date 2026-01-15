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
        catch
        {
            // If settings are corrupted, use defaults
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
        catch
        {
            // Ignore save errors
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
    }
}
