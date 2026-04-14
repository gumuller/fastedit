using System.Windows.Input;

namespace FastEdit.Infrastructure;

public record CommandDescriptor(
    string Name,
    string Category,
    string? ShortcutText,
    ICommand Command,
    object? CommandParameter = null);

/// <summary>
/// Central registry of all commands for the command palette.
/// </summary>
public class CommandRegistry
{
    private readonly List<CommandDescriptor> _commands = new();

    public IReadOnlyList<CommandDescriptor> Commands => _commands;

    public void Register(string name, string category, string? shortcut, ICommand command, object? parameter = null)
    {
        _commands.Add(new CommandDescriptor(name, category, shortcut, command, parameter));
    }

    public IEnumerable<CommandDescriptor> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _commands;

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return _commands.Where(cmd => terms.All(term =>
            cmd.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            cmd.Category.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }
}
