using System.IO;

namespace FastEdit.Infrastructure;

public static class SyntaxLanguageResolver
{
    private static readonly IReadOnlyDictionary<string, string> FileNameLanguages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dockerfile"] = "Dockerfile",
            ["Containerfile"] = "Dockerfile",
            ["Makefile"] = "Makefile",
            ["GNUmakefile"] = "Makefile",
            [".gitignore"] = "INI",
            [".gitattributes"] = "INI",
            [".gitmodules"] = "INI",
            [".editorconfig"] = "INI",
            [".env"] = "INI",
            [".env.local"] = "INI",
            [".env.production"] = "INI",
            ["CMakeLists.txt"] = "CMake",
        };

    private static readonly IReadOnlyDictionary<string, string> ExtensionLanguages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = "C#",
            [".js"] = "JavaScript",
            [".mjs"] = "JavaScript",
            [".cjs"] = "JavaScript",
            [".ts"] = "TypeScript",
            [".tsx"] = "TypeScript",
            [".py"] = "Python",
            [".pyw"] = "Python",
            [".java"] = "Java",
            [".cpp"] = "C++",
            [".cxx"] = "C++",
            [".cc"] = "C++",
            [".hpp"] = "C++",
            [".hxx"] = "C++",
            [".c"] = "C",
            [".h"] = "C",
            [".rs"] = "Rust",
            [".go"] = "Go",
            [".rb"] = "Ruby",
            [".rake"] = "Ruby",
            [".html"] = "HTML",
            [".htm"] = "HTML",
            [".css"] = "CSS",
            [".scss"] = "CSS",
            [".less"] = "CSS",
            [".xml"] = "XML",
            [".xaml"] = "XML",
            [".xsl"] = "XML",
            [".xsd"] = "XML",
            [".csproj"] = "XML",
            [".fsproj"] = "XML",
            [".vbproj"] = "XML",
            [".props"] = "XML",
            [".targets"] = "XML",
            [".json"] = "JSON",
            [".jsonc"] = "JSON",
            [".yaml"] = "YAML",
            [".yml"] = "YAML",
            [".md"] = "Markdown",
            [".markdown"] = "Markdown",
            [".sql"] = "SQL",
            [".ps1"] = "PowerShell",
            [".psm1"] = "PowerShell",
            [".psd1"] = "PowerShell",
            [".sh"] = "Shell",
            [".bash"] = "Shell",
            [".zsh"] = "Shell",
            [".fish"] = "Shell",
            [".bat"] = "Batch",
            [".cmd"] = "Batch",
            [".toml"] = "TOML",
            [".ini"] = "INI",
            [".cfg"] = "INI",
            [".conf"] = "INI",
        };

    public static string Resolve(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (FileNameLanguages.TryGetValue(fileName, out var language))
            return language;

        return ExtensionLanguages.TryGetValue(Path.GetExtension(filePath), out language)
            ? language
            : string.Empty;
    }
}
