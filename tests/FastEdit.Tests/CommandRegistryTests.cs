using FastEdit.Infrastructure;
using System.Windows.Input;

namespace FastEdit.Tests;

public class CommandRegistryTests
{
    private CommandRegistry CreateRegistryWithSampleCommands()
    {
        var registry = new CommandRegistry();
        registry.Register("New File", "File", "Ctrl+N", new TestCommand());
        registry.Register("Open File", "File", "Ctrl+O", new TestCommand());
        registry.Register("Save", "File", "Ctrl+S", new TestCommand());
        registry.Register("Find", "Edit", "Ctrl+F", new TestCommand());
        registry.Register("Replace", "Edit", "Ctrl+H", new TestCommand());
        registry.Register("Toggle Word Wrap", "View", null, new TestCommand());
        registry.Register("Zoom In", "View", "Ctrl++", new TestCommand());
        return registry;
    }

    [Fact]
    public void Register_Adds_Commands()
    {
        var registry = CreateRegistryWithSampleCommands();
        Assert.Equal(7, registry.Commands.Count);
    }

    [Fact]
    public void Search_Empty_Query_Returns_All()
    {
        var registry = CreateRegistryWithSampleCommands();
        var results = registry.Search("").ToList();
        Assert.Equal(7, results.Count);
    }

    [Fact]
    public void Search_Null_Query_Returns_All()
    {
        var registry = CreateRegistryWithSampleCommands();
        var results = registry.Search(null!).ToList();
        Assert.Equal(7, results.Count);
    }

    [Fact]
    public void Search_By_Name_Finds_Match()
    {
        var registry = CreateRegistryWithSampleCommands();
        var results = registry.Search("Find").ToList();
        Assert.Contains(results, r => r.Name == "Find");
    }

    [Fact]
    public void Search_By_Category_Finds_Matches()
    {
        var registry = CreateRegistryWithSampleCommands();
        var results = registry.Search("File").ToList();
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void Search_Is_Case_Insensitive()
    {
        var registry = CreateRegistryWithSampleCommands();
        var results = registry.Search("zoom").ToList();
        Assert.Single(results);
        Assert.Equal("Zoom In", results[0].Name);
    }

    [Fact]
    public void Search_Multiple_Terms_Narrows_Results()
    {
        var registry = CreateRegistryWithSampleCommands();
        var results = registry.Search("File Open").ToList();
        Assert.Single(results);
        Assert.Equal("Open File", results[0].Name);
    }

    [Fact]
    public void CommandDescriptor_Has_All_Properties()
    {
        var cmd = new TestCommand();
        var registry = new CommandRegistry();
        registry.Register("Test Cmd", "Cat", "Ctrl+T", cmd, "param");

        var descriptor = registry.Commands[0];
        Assert.Equal("Test Cmd", descriptor.Name);
        Assert.Equal("Cat", descriptor.Category);
        Assert.Equal("Ctrl+T", descriptor.ShortcutText);
        Assert.Same(cmd, descriptor.Command);
        Assert.Equal("param", descriptor.CommandParameter);
    }

    [Fact]
    public void Search_No_Match_Returns_Empty()
    {
        var registry = CreateRegistryWithSampleCommands();
        var results = registry.Search("xyznonexistent").ToList();
        Assert.Empty(results);
    }

    private class TestCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }
}
