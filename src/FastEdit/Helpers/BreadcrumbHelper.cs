using System.Text.RegularExpressions;

namespace FastEdit.Helpers;

public static class BreadcrumbHelper
{
    public record BreadcrumbItem(string Name, string Kind, int Line);

    public static List<BreadcrumbItem> GetBreadcrumbs(string documentText, int caretLine, string? language)
    {
        if (string.IsNullOrEmpty(documentText) || caretLine < 1)
            return [];

        var lines = documentText.Split('\n');
        if (caretLine > lines.Length)
            caretLine = lines.Length;

        return language switch
        {
            "Python" => GetPythonBreadcrumbs(lines, caretLine),
            "Go" => GetBraceBreadcrumbs(lines, caretLine, GoPatterns),
            "Rust" => GetBraceBreadcrumbs(lines, caretLine, RustPatterns),
            "C#" or "Java" or "C++" or "C" => GetBraceBreadcrumbs(lines, caretLine, CSharpJavaPatterns),
            "JavaScript" or "TypeScript" => GetBraceBreadcrumbs(lines, caretLine, JsTsPatterns),
            _ => GetBraceBreadcrumbs(lines, caretLine, FallbackPatterns)
        };
    }

    private record DeclarationPattern(Regex Regex, string Kind);

    private static readonly DeclarationPattern[] CSharpJavaPatterns =
    [
        new(new Regex(@"^\s*(?:(?:public|private|protected|internal|static|abstract|sealed|partial|readonly)\s+)*namespace\s+([\w.]+)", RegexOptions.Compiled), "namespace"),
        new(new Regex(@"^\s*(?:(?:public|private|protected|internal|static|abstract|sealed|partial)\s+)*(?:class|record)\s+(\w+)", RegexOptions.Compiled), "class"),
        new(new Regex(@"^\s*(?:(?:public|private|protected|internal|static|abstract|sealed|partial)\s+)*(?:struct)\s+(\w+)", RegexOptions.Compiled), "struct"),
        new(new Regex(@"^\s*(?:(?:public|private|protected|internal|static|abstract|sealed|partial)\s+)*interface\s+(\w+)", RegexOptions.Compiled), "interface"),
        new(new Regex(@"^\s*(?:(?:public|private|protected|internal|static|abstract|sealed|partial)\s+)*enum\s+(\w+)", RegexOptions.Compiled), "enum"),
        new(new Regex(@"^\s*(?:(?:public|private|protected|internal|static|abstract|sealed|partial|virtual|override|async|new)\s+)*(?:[\w<>\[\]?,\s]+)\s+(\w+)\s*\(", RegexOptions.Compiled), "method"),
    ];

    private static readonly DeclarationPattern[] JsTsPatterns =
    [
        new(new Regex(@"^\s*(?:export\s+)?class\s+(\w+)", RegexOptions.Compiled), "class"),
        new(new Regex(@"^\s*(?:export\s+)?(?:async\s+)?function\s+(\w+)\s*\(", RegexOptions.Compiled), "function"),
        new(new Regex(@"^\s*(?:export\s+)?(?:const|let|var)\s+(\w+)\s*=\s*(?:async\s+)?\(", RegexOptions.Compiled), "function"),
        new(new Regex(@"^\s*(?:export\s+)?(?:const|let|var)\s+(\w+)\s*=\s*(?:async\s+)?(?:function)", RegexOptions.Compiled), "function"),
        new(new Regex(@"^\s*(?:(?:public|private|protected|static|async|get|set)\s+)*(\w+)\s*\(", RegexOptions.Compiled), "method"),
    ];

    private static readonly DeclarationPattern[] GoPatterns =
    [
        new(new Regex(@"^\s*package\s+(\w+)", RegexOptions.Compiled), "package"),
        new(new Regex(@"^\s*func\s+\(\s*\w+\s+\*?(\w+)\)\s+(\w+)\s*\(", RegexOptions.Compiled), "method"),
        new(new Regex(@"^\s*func\s+(\w+)\s*\(", RegexOptions.Compiled), "func"),
        new(new Regex(@"^\s*type\s+(\w+)\s+struct", RegexOptions.Compiled), "struct"),
        new(new Regex(@"^\s*type\s+(\w+)\s+interface", RegexOptions.Compiled), "interface"),
    ];

    private static readonly DeclarationPattern[] RustPatterns =
    [
        new(new Regex(@"^\s*(?:pub(?:\(crate\))?\s+)?mod\s+(\w+)", RegexOptions.Compiled), "mod"),
        new(new Regex(@"^\s*(?:pub(?:\(crate\))?\s+)?struct\s+(\w+)", RegexOptions.Compiled), "struct"),
        new(new Regex(@"^\s*(?:pub(?:\(crate\))?\s+)?enum\s+(\w+)", RegexOptions.Compiled), "enum"),
        new(new Regex(@"^\s*(?:pub(?:\(crate\))?\s+)?trait\s+(\w+)", RegexOptions.Compiled), "trait"),
        new(new Regex(@"^\s*impl(?:<[^>]*>)?\s+(\w+)", RegexOptions.Compiled), "impl"),
        new(new Regex(@"^\s*(?:pub(?:\(crate\))?\s+)?(?:async\s+)?fn\s+(\w+)\s*[<(]", RegexOptions.Compiled), "fn"),
    ];

    private static readonly DeclarationPattern[] FallbackPatterns =
    [
        new(new Regex(@"^\s*(?:class|struct|interface|enum)\s+(\w+)", RegexOptions.Compiled), "type"),
        new(new Regex(@"^\s*(?:function|func|fn|def|sub|proc)\s+(\w+)\s*[<(]", RegexOptions.Compiled), "function"),
    ];

    private static List<BreadcrumbItem> GetBraceBreadcrumbs(string[] lines, int caretLine, DeclarationPattern[] patterns)
    {
        // Stack of (BreadcrumbItem, braceDepthWhenDeclared)
        var scopeStack = new List<(BreadcrumbItem Item, int Depth)>();
        int braceDepth = 0;

        for (int i = 0; i < caretLine; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // Check for declarations before counting braces on this line
            var match = TryMatchDeclaration(line, patterns);
            if (match != null)
            {
                // Pop scopes that are at same or deeper depth (sibling or child replaced)
                while (scopeStack.Count > 0 && scopeStack[^1].Depth >= braceDepth)
                    scopeStack.RemoveAt(scopeStack.Count - 1);

                scopeStack.Add((new BreadcrumbItem(match.Value.Name, match.Value.Kind, i + 1), braceDepth));
            }

            // Count braces (skip strings/comments simplistically)
            bool inString = false;
            char stringChar = '\0';
            for (int c = 0; c < line.Length; c++)
            {
                char ch = line[c];
                if (inString)
                {
                    if (ch == stringChar && (c == 0 || line[c - 1] != '\\'))
                        inString = false;
                    continue;
                }
                if (ch == '"' || ch == '\'')
                {
                    inString = true;
                    stringChar = ch;
                    continue;
                }
                // Skip line comments
                if (ch == '/' && c + 1 < line.Length && line[c + 1] == '/')
                    break;

                if (ch == '{')
                    braceDepth++;
                else if (ch == '}')
                {
                    braceDepth--;
                    // Pop scopes that have exited
                    while (scopeStack.Count > 0 && scopeStack[^1].Depth >= braceDepth)
                        scopeStack.RemoveAt(scopeStack.Count - 1);
                }
            }
        }

        return scopeStack.Select(s => s.Item).ToList();
    }

    private static (string Name, string Kind)? TryMatchDeclaration(string line, DeclarationPattern[] patterns)
    {
        foreach (var pattern in patterns)
        {
            var m = pattern.Regex.Match(line);
            if (m.Success)
            {
                // For Go methods with receiver: "func (r *Type) Method(" - use "Type.Method"
                if (pattern.Kind == "method" && m.Groups.Count >= 3)
                    return ($"{m.Groups[1].Value}.{m.Groups[2].Value}", pattern.Kind);

                return (m.Groups[1].Value, pattern.Kind);
            }
        }
        return null;
    }

    private static List<BreadcrumbItem> GetPythonBreadcrumbs(string[] lines, int caretLine)
    {
        var pythonPatterns = new DeclarationPattern[]
        {
            new(new Regex(@"^(\s*)class\s+(\w+)", RegexOptions.Compiled), "class"),
            new(new Regex(@"^(\s*)(?:async\s+)?def\s+(\w+)\s*\(", RegexOptions.Compiled), "def"),
        };

        // Stack of (BreadcrumbItem, indentLevel)
        var scopeStack = new List<(BreadcrumbItem Item, int Indent)>();

        for (int i = 0; i < caretLine; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            foreach (var pattern in pythonPatterns)
            {
                var m = pattern.Regex.Match(line);
                if (m.Success)
                {
                    int indent = m.Groups[1].Value.Length;
                    string name = m.Groups[2].Value;

                    // Pop scopes at same or deeper indent
                    while (scopeStack.Count > 0 && scopeStack[^1].Indent >= indent)
                        scopeStack.RemoveAt(scopeStack.Count - 1);

                    scopeStack.Add((new BreadcrumbItem(name, pattern.Kind, i + 1), indent));
                    break;
                }
            }
        }

        return scopeStack.Select(s => s.Item).ToList();
    }
}
