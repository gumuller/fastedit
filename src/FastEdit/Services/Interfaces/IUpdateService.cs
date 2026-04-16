using FastEdit.Models;

namespace FastEdit.Services.Interfaces;

public interface IUpdateService
{
    Task<UpdateInfo?> CheckForUpdatesAsync();
}
