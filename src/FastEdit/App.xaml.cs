using System.Windows;
using FastEdit.Services;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;
using FastEdit.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FastEdit;

public partial class App : Application
{
    private IHost? _host;

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Services
                services.AddSingleton<ISettingsService, SettingsService>();
                services.AddSingleton<IThemeService, ThemeService>();
                services.AddSingleton<IFileService, FileService>();

                // ViewModels
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<FileTreeViewModel>();
                services.AddTransient<EditorTabViewModel>();

                // Views
                services.AddSingleton<MainWindow>();
            })
            .Build();

        Services = _host.Services;

        // Apply saved theme from settings
        var settingsService = Services.GetRequiredService<ISettingsService>();
        var themeService = Services.GetRequiredService<IThemeService>();
        themeService.ApplyTheme(settingsService.ThemeName);

        // Show main window
        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
