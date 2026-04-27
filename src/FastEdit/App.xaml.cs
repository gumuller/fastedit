using System.Windows;
using System.Diagnostics;
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

    /// <summary>Command-line file paths passed to the app (e.g. from Explorer "Open with").</summary>
    public static IReadOnlyList<string> StartupFiles { get; private set; } = Array.Empty<string>();
    public static bool HasAnotherRunningInstance { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        HasAnotherRunningInstance = DetectAnotherRunningInstance();

        // Capture any file paths passed on the command line (e.g. Explorer
        // "Open with FastEdit" passes the clicked file as the first arg).
        // Filter to args that look like existing files — anything else is
        // either a switch or malformed and should be ignored silently.
        if (e.Args is { Length: > 0 })
        {
            var files = new List<string>(e.Args.Length);
            foreach (var arg in e.Args)
            {
                if (string.IsNullOrWhiteSpace(arg)) continue;
                if (arg.StartsWith('-') || arg.StartsWith('/')) continue;
                try
                {
                    var full = System.IO.Path.GetFullPath(arg);
                    if (System.IO.File.Exists(full))
                        files.Add(full);
                }
                catch { /* invalid path — ignore */ }
            }
            StartupFiles = files;
        }

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
                services.AddSingleton<ILineFilterService, LineFilterService>();

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

    private static bool DetectAnotherRunningInstance()
    {
        using var currentProcess = Process.GetCurrentProcess();

        foreach (var process in Process.GetProcessesByName(currentProcess.ProcessName))
        {
            using (process)
            {
                if (process.Id == currentProcess.Id)
                {
                    continue;
                }

                try
                {
                    if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                    {
                        return true;
                    }
                }
                catch (InvalidOperationException ex)
                {
                    Trace.TraceWarning("Failed to inspect running FastEdit process '{0}': {1}", process.Id, ex.Message);
                }
            }
        }

        return false;
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
