using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public sealed class CommandRunnerFactory : ICommandRunnerFactory
{
    public ICommandRunner Create() => new CommandRunnerService();
}
