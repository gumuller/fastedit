using System.Windows;
using FastEdit.Helpers;
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

        // Global exception handlers for debugging
        DispatcherUnhandledException += (s, args) =>
        {
            System.Diagnostics.Trace.TraceError($"Unhandled UI exception: {args.Exception}");
            MessageBox.Show($"Error: {args.Exception.Message}\n\n{args.Exception.StackTrace}",
                "FastEdit Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                System.Diagnostics.Trace.TraceError($"Unhandled domain exception: {ex}");
        };

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Core services
                services.AddSingleton<ISettingsService, SettingsService>();
                services.AddSingleton<IThemeService, ThemeService>();
                services.AddSingleton<IFileService, FileService>();
                services.AddSingleton<IFileSystemService, FileSystemService>();
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton<ITextToolsService, TextToolsService>();
                services.AddSingleton<AutoSaveService>();
                services.AddSingleton<IAutoSaveService>(sp => sp.GetRequiredService<AutoSaveService>());
                services.AddSingleton<IWorkspaceService, WorkspaceService>();
                services.AddSingleton<IMacroService, MacroService>();
                services.AddSingleton<IDispatcherService, WpfDispatcherService>();
                services.AddSingleton<IProcessService, ProcessService>();
                services.AddTransient<IFileWatcherService, FileWatcherService>();
                services.AddSingleton<IUpdateService, UpdateService>();
                services.AddSingleton<IGitService, GitService>();
                services.AddSingleton<IKeyBindingService, KeyBindingService>();

                // Factories
                services.AddSingleton<IEditorTabFactory, EditorTabFactory>();

                // ViewModels
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<FileTreeViewModel>();
                services.AddTransient<EditorTabViewModel>();
                services.AddTransient<FindInFilesViewModel>();

                // Views
                services.AddSingleton<MainWindow>();
            })
            .Build();

        Services = _host.Services;

        // Register custom syntax highlighting definitions
        SyntaxHighlightingRegistrar.RegisterAll();

        // Apply saved theme from settings
        var settingsService = Services.GetRequiredService<ISettingsService>();
        var themeService = Services.GetRequiredService<IThemeService>();
        themeService.ApplyTheme(settingsService.ThemeName);

        // Start main window
        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        // Start auto-save service
        var autoSave = Services.GetRequiredService<AutoSaveService>();
        var mainVm = Services.GetRequiredService<MainViewModel>();
        autoSave.SetEntryProvider(() => mainVm.GetAutoSaveEntries());
        autoSave.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var autoSave = Services.GetService<AutoSaveService>();
        autoSave?.Stop();
        autoSave?.MarkCleanShutdown();

        _host?.Dispose();
        base.OnExit(e);
    }
}
