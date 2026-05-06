using System.IO;

namespace FastEdit.Helpers;

public static class FileIconHelper
{
    private const string FolderIcon = "\uE8B7";
    private const string CodeIcon = "\uE943";
    private const string WebIcon = "\uE774";
    private const string StyleIcon = "\uE790";
    private const string StructuredDataIcon = "\uE9D5";
    private const string DocumentIcon = "\uE8A5";
    private const string ImageIcon = "\uEB9F";
    private const string PdfIcon = "\uEA90";
    private const string ArchiveIcon = "\uF012";
    private const string AppIcon = "\uE7AC";
    private const string TerminalIcon = "\uE756";
    private const string DatabaseIcon = "\uE964";
    private const string SettingsIcon = "\uE713";

    private static readonly IReadOnlyDictionary<string, string> IconsByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = CodeIcon,
            [".js"] = CodeIcon,
            [".ts"] = CodeIcon,
            [".tsx"] = CodeIcon,
            [".jsx"] = CodeIcon,
            [".py"] = CodeIcon,
            [".java"] = CodeIcon,
            [".cpp"] = CodeIcon,
            [".c"] = CodeIcon,
            [".h"] = CodeIcon,
            [".hpp"] = CodeIcon,
            [".rs"] = CodeIcon,
            [".go"] = CodeIcon,
            [".html"] = WebIcon,
            [".htm"] = WebIcon,
            [".css"] = StyleIcon,
            [".scss"] = StyleIcon,
            [".sass"] = StyleIcon,
            [".less"] = StyleIcon,
            [".json"] = StructuredDataIcon,
            [".xml"] = StructuredDataIcon,
            [".xaml"] = StructuredDataIcon,
            [".csproj"] = StructuredDataIcon,
            [".sln"] = StructuredDataIcon,
            [".yaml"] = StructuredDataIcon,
            [".yml"] = StructuredDataIcon,
            [".toml"] = StructuredDataIcon,
            [".md"] = DocumentIcon,
            [".txt"] = DocumentIcon,
            [".log"] = DocumentIcon,
            [".csv"] = DocumentIcon,
            [".png"] = ImageIcon,
            [".jpg"] = ImageIcon,
            [".jpeg"] = ImageIcon,
            [".gif"] = ImageIcon,
            [".bmp"] = ImageIcon,
            [".svg"] = ImageIcon,
            [".ico"] = ImageIcon,
            [".webp"] = ImageIcon,
            [".pdf"] = PdfIcon,
            [".zip"] = ArchiveIcon,
            [".tar"] = ArchiveIcon,
            [".gz"] = ArchiveIcon,
            [".7z"] = ArchiveIcon,
            [".rar"] = ArchiveIcon,
            [".exe"] = AppIcon,
            [".dll"] = AppIcon,
            [".msi"] = AppIcon,
            [".ps1"] = TerminalIcon,
            [".bat"] = TerminalIcon,
            [".cmd"] = TerminalIcon,
            [".sh"] = TerminalIcon,
            [".bash"] = TerminalIcon,
            [".sql"] = DatabaseIcon,
            [".gitignore"] = SettingsIcon,
            [".editorconfig"] = SettingsIcon,
            [".env"] = SettingsIcon,
        };

    private static readonly IReadOnlyDictionary<string, string> ColorsByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = "#68217A",
            [".js"] = "#F7DF1E",
            [".ts"] = "#3178C6",
            [".tsx"] = "#3178C6",
            [".py"] = "#3776AB",
            [".json"] = "#CBB148",
            [".html"] = "#E44D26",
            [".htm"] = "#E44D26",
            [".css"] = "#264DE4",
            [".md"] = "#083FA1",
            [".xml"] = "#FF6600",
            [".xaml"] = "#FF6600",
            [".yaml"] = "#CB171E",
            [".yml"] = "#CB171E",
            [".rs"] = "#DEA584",
            [".go"] = "#00ADD8",
            [".java"] = "#ED8B00",
            [".ps1"] = "#012456",
        };

    public static string GetIcon(string fileName, bool isDirectory = false)
    {
        if (isDirectory) return FolderIcon;

        return IconsByExtension.TryGetValue(Path.GetExtension(fileName), out var icon)
            ? icon
            : DocumentIcon;
    }

    public static string? GetIconColor(string fileName)
    {
        return ColorsByExtension.TryGetValue(Path.GetExtension(fileName), out var color)
            ? color
            : null;
    }
}
