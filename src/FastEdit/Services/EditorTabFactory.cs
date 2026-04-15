using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FastEdit.Services;

public class EditorTabFactory : IEditorTabFactory
{
    private readonly IServiceProvider _serviceProvider;

    public EditorTabFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public EditorTabViewModel Create()
    {
        return _serviceProvider.GetRequiredService<EditorTabViewModel>();
    }

    public EditorTabViewModel CreateFromPath(string filePath)
    {
        var tab = Create();
        tab.FilePath = filePath;
        tab.FileName = System.IO.Path.GetFileName(filePath);
        return tab;
    }

    public EditorTabViewModel CreateUntitled(string? content = null)
    {
        var tab = Create();
        tab.FileName = "Untitled";
        if (content != null)
        {
            tab.Content = content;
        }
        return tab;
    }
}
