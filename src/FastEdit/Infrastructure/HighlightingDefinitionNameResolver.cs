namespace FastEdit.Infrastructure;

public static class HighlightingDefinitionNameResolver
{
    private static readonly IReadOnlyDictionary<string, string> DefinitionNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["C#"] = "C#",
            ["JavaScript"] = "JavaScript",
            ["TypeScript"] = "JavaScript",
            ["Python"] = "Python",
            ["Java"] = "Java",
            ["C++"] = "C++",
            ["C"] = "C++",
            ["HTML"] = "HTML",
            ["CSS"] = "CSS",
            ["XML"] = "XML",
            ["JSON"] = "Json",
            ["SQL"] = "TSQL",
            ["PowerShell"] = "PowerShell",
            ["Markdown"] = "MarkDown",
            ["YAML"] = "YAML",
            ["Shell"] = "Bash",
            ["Dockerfile"] = "Dockerfile",
            ["Rust"] = "Rust",
            ["Go"] = "Go",
            ["TOML"] = "TOML",
            ["INI"] = "INI",
        };

    public static string? Resolve(string language) =>
        DefinitionNames.TryGetValue(language, out var definitionName) ? definitionName : null;
}
