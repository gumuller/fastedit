using System.IO;

namespace FastEdit.Helpers;

public static class FileIconHelper
{
    public static string GetIcon(string fileName, bool isDirectory = false)
    {
        if (isDirectory) return "\uE8B7"; // folder icon

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "\uE943",      // code
            ".js" or ".ts" or ".tsx" or ".jsx" => "\uE943",
            ".py" => "\uE943",
            ".java" => "\uE943",
            ".cpp" or ".c" or ".h" or ".hpp" => "\uE943",
            ".rs" or ".go" => "\uE943",
            ".html" or ".htm" => "\uE774",  // web
            ".css" or ".scss" or ".sass" or ".less" => "\uE790", // paint
            ".json" => "\uE9D5",    // code block
            ".xml" or ".xaml" or ".csproj" or ".sln" => "\uE9D5",
            ".yaml" or ".yml" or ".toml" => "\uE9D5",
            ".md" or ".txt" or ".log" or ".csv" => "\uE8A5", // document
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" or ".ico" or ".webp" => "\uEB9F", // image
            ".pdf" => "\uEA90",     // PDF
            ".zip" or ".tar" or ".gz" or ".7z" or ".rar" => "\uF012", // archive
            ".exe" or ".dll" or ".msi" => "\uE7AC", // app
            ".ps1" or ".bat" or ".cmd" or ".sh" or ".bash" => "\uE756", // terminal
            ".sql" => "\uE964",     // database
            ".gitignore" or ".editorconfig" or ".env" => "\uE713", // settings gear
            _ => "\uE8A5"           // generic document
        };
    }

    public static string? GetIconColor(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "#68217A",       // C# purple
            ".js" => "#F7DF1E",       // JS yellow
            ".ts" or ".tsx" => "#3178C6", // TS blue
            ".py" => "#3776AB",       // Python blue
            ".json" => "#CBB148",     // JSON yellow
            ".html" or ".htm" => "#E44D26", // HTML orange
            ".css" => "#264DE4",      // CSS blue
            ".md" => "#083FA1",       // Markdown blue
            ".xml" or ".xaml" => "#FF6600", // XML orange
            ".yaml" or ".yml" => "#CB171E", // YAML red
            ".rs" => "#DEA584",       // Rust orange
            ".go" => "#00ADD8",       // Go blue
            ".java" => "#ED8B00",     // Java orange
            ".ps1" => "#012456",      // PowerShell dark blue
            _ => null
        };
    }
}
