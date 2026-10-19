using System.IO;
using FastEdit.Services;
using Microsoft.Win32;

namespace FastEdit.Tests;

public sealed class ShellIntegrationServiceTests : IDisposable
{
    private readonly string _className = $"FastEdit.Tests.{Guid.NewGuid():N}";
    private readonly string _directory = Directory.CreateTempSubdirectory("fastedit shell tests ").FullName;
    private int _notifications;

    private string VerbPath => _className + @"\shell\FastEdit";
    private string UserVerbPath => @"Software\Classes\" + VerbPath;
    private string Executable => Path.Combine(_directory, "FastEdit.exe");

    public ShellIntegrationServiceTests() => File.WriteAllBytes(Executable, Array.Empty<byte>());

    private ShellIntegrationService CreateService(string? executable = null) =>
        new(executable ?? Executable, VerbPath, () => _notifications++);

    [Fact]
    public void Enable_Registers_Quoted_Command_And_Embedded_Icon()
    {
        var service = CreateService();

        service.SetEnabled(true);

        Assert.True(service.IsEnabled);
        using var verb = Registry.CurrentUser.OpenSubKey(UserVerbPath);
        Assert.NotNull(verb);
        Assert.Equal("Edit with FastEdit", verb.GetValue(null));
        Assert.Equal($"\"{Executable}\",0", verb.GetValue("Icon"));
        Assert.Equal(Executable, verb.GetValue("FastEditExecutable"));
        using var command = verb.OpenSubKey("command");
        Assert.NotNull(command);
        Assert.Equal($"\"{Executable}\" \"%1\"", command.GetValue(null));
        Assert.Equal(1, _notifications);
    }

    [Fact]
    public void Disable_Hides_The_Entry_Without_Changing_Other_Verbs()
    {
        var service = CreateService();
        service.SetEnabled(true);
        using (var other = Registry.CurrentUser.CreateSubKey(
                   @"Software\Classes\" + _className + @"\shell\OtherApp"))
            other.SetValue(null, "Keep this command");

        service.SetEnabled(false);

        Assert.False(service.IsEnabled);
        using var verb = Registry.CurrentUser.OpenSubKey(UserVerbPath);
        Assert.NotNull(verb);
        Assert.Equal(string.Empty, verb.GetValue("LegacyDisable"));
        using var otherVerb = Registry.CurrentUser.OpenSubKey(
            @"Software\Classes\" + _className + @"\shell\OtherApp");
        Assert.NotNull(otherVerb);
        Assert.Equal("Keep this command", otherVerb.GetValue(null));
        Assert.Equal(2, _notifications);
    }

    [Fact]
    public void Reenable_Updates_A_Moved_Portable_Copy_And_Clears_OptOut()
    {
        var original = CreateService();
        original.SetEnabled(true);
        original.SetEnabled(false);
        var movedPath = Path.Combine(_directory, "moved FastEdit.exe");
        File.WriteAllBytes(movedPath, Array.Empty<byte>());
        var moved = CreateService(movedPath);

        moved.SetEnabled(true);

        Assert.True(moved.IsEnabled);
        Assert.False(original.IsEnabled);
        using var verb = Registry.CurrentUser.OpenSubKey(UserVerbPath);
        Assert.NotNull(verb);
        Assert.Null(verb.GetValue("LegacyDisable"));
        Assert.Equal(movedPath, verb.GetValue("FastEditExecutable"));
    }

    [Fact]
    public void Missing_Executable_Does_Not_Replace_An_Existing_Registration()
    {
        var original = CreateService();
        original.SetEnabled(true);
        var missing = CreateService(Path.Combine(_directory, "missing.exe"));

        Assert.Throws<FileNotFoundException>(() => missing.SetEnabled(true));

        Assert.True(original.IsEnabled);
        Assert.Equal(1, _notifications);
    }

    [Fact]
    public void Query_Recognizes_An_Existing_Installer_Registration()
    {
        using (var verb = Registry.CurrentUser.CreateSubKey(UserVerbPath))
        {
            verb.SetValue(null, "Open with FastEdit");
            using var command = verb.CreateSubKey("command");
            command.SetValue(null, $"\"{Executable}\" \"%1\"");
        }

        Assert.True(CreateService().IsEnabled);
    }

    [Theory]
    [InlineData("LegacyDisable")]
    [InlineData("ProgrammaticAccessOnly")]
    public void Query_Recognizes_A_Hidden_Context_Menu_Entry(string flag)
    {
        var service = CreateService();
        service.SetEnabled(true);
        using (var verb = Registry.CurrentUser.OpenSubKey(UserVerbPath, writable: true))
        {
            Assert.NotNull(verb);
            verb.SetValue(flag, string.Empty);
        }

        Assert.False(service.IsEnabled);
    }

    [Fact]
    public void Disable_Does_Not_Hide_Another_Copy()
    {
        var original = CreateService();
        original.SetEnabled(true);
        var other = CreateService(Path.Combine(_directory, "another.exe"));

        other.SetEnabled(false);

        Assert.True(original.IsEnabled);
        Assert.Equal(1, _notifications);
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + _className, throwOnMissingSubKey: false);
        Directory.Delete(_directory, recursive: true);
    }
}
