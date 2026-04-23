using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace FastEdit.Views.Dialogs;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        // Read the file version from the .exe so the displayed version always
        // tracks the build counter (AssemblyName.Version can report 1.1.0 when
        // the version-inject target didn't run, e.g. under some test harnesses).
        string versionStr;
        try
        {
            var exePath = System.Reflection.Assembly.GetEntryAssembly()?.Location
                          ?? typeof(AboutDialog).Assembly.Location;
            var vi = FileVersionInfo.GetVersionInfo(exePath);
            var parts = (vi.FileVersion ?? "").Split('.');
            versionStr = parts.Length >= 3
                ? $"Version {parts[0]}.{parts[1]}.{parts[2]}"
                : $"Version {vi.FileVersion}";
        }
        catch
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            versionStr = v != null ? $"Version {v.Major}.{v.Minor}.{v.Build}" : "Version 1.0.0";
        }
        VersionText.Text = versionStr;
    }

    private void GitHub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/gumuller/fastedit",
                UseShellExecute = true,
            });
        }
        catch { /* user can open manually */ }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();
}
