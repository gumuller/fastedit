namespace FastEdit.Services.Interfaces;

public interface IKeyBindingService
{
    Dictionary<string, string> GetBindings();
    void SetBinding(string command, string gesture);
    void ResetToDefaults();
}
