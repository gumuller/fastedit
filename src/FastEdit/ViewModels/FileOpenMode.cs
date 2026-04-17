namespace FastEdit.ViewModels;

/// <summary>
/// How a file is loaded into an editor tab.
/// </summary>
public enum FileOpenMode
{
    /// <summary>Normal AvalonEdit text document (in-memory, full-featured).</summary>
    Text,

    /// <summary>Binary/hex editor with VirtualizedByteBuffer (MMF-backed).</summary>
    Binary,

    /// <summary>Large read-only text viewer with sparse-indexed MMF (100MB+ files).</summary>
    LargeText
}
