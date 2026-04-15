using FastEdit.ViewModels;

namespace FastEdit.Services.Interfaces;

public interface IEditorTabFactory
{
    EditorTabViewModel Create();
    EditorTabViewModel CreateFromPath(string filePath);
    EditorTabViewModel CreateUntitled(string? content = null);
}
