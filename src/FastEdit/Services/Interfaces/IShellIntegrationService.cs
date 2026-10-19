namespace FastEdit.Services.Interfaces;

public interface IShellIntegrationService
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}
