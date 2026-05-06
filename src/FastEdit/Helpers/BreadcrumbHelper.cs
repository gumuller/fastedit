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

    private static readonly DeclarationPattern[] PythonPatterns =
    [
        new(new Regex(@"^(\s*)class\s+(\w+)", RegexOptions.Compiled), "class"),
        new(new Regex(@"^(\s*)(?:async\s+)?def\s+(\w+)\s*\(", RegexOptions.Compiled), "def"),
    ];

    private static List<BreadcrumbItem> GetBraceBreadcrumbs(string[] lines, int caretLine, DeclarationPattern[] patterns)
    {
        var scopeStack = new List<(BreadcrumbItem Item, int Depth)>();
        var braceDepth = 0;

        for (var i = 0; i < caretLine; i++)
        {
            var line = lines[i].TrimEnd('\r');
            AddBraceDeclarationScope(scopeStack, line, patterns, i + 1, braceDepth);
            braceDepth = ApplyBraceDepth(line, braceDepth, scopeStack);
        }

        return scopeStack.Select(s => s.Item).ToList();
    }

    private static void AddBraceDeclarationScope(
        List<(BreadcrumbItem Item, int Depth)> scopeStack,
        string line,
        DeclarationPattern[] patterns,
        int lineNumber,
        int braceDepth)
    {
        var match = TryMatchDeclaration(line, patterns);
        if (match == null)
            return;

        RemoveScopesAtOrBelowDepth(scopeStack, braceDepth);
        scopeStack.Add((new BreadcrumbItem(match.Value.Name, match.Value.Kind, lineNumber), braceDepth));
    }

    private static int ApplyBraceDepth(
        string line,
        int braceDepth,
        List<(BreadcrumbItem Item, int Depth)> scopeStack)
    {
        var scanner = new BreadcrumbBraceScanner(line);
        while (scanner.TryRead(out var token))
        {
            if (token == BreadcrumbBraceToken.Open)
                braceDepth++;
            else if (token == BreadcrumbBraceToken.Close)
                RemoveScopesAtOrBelowDepth(scopeStack, --braceDepth);
        }

        return braceDepth;
    }

    private static void RemoveScopesAtOrBelowDepth(List<(BreadcrumbItem Item, int Depth)> scopeStack, int braceDepth)
    {
        while (scopeStack.Count > 0 && scopeStack[^1].Depth >= braceDepth)
            scopeStack.RemoveAt(scopeStack.Count - 1);
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
        var scopeStack = new List<(BreadcrumbItem Item, int Indent)>();

        for (var i = 0; i < caretLine; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            AddPythonDeclarationScope(scopeStack, line, i + 1);
        }

        return scopeStack.Select(s => s.Item).ToList();
    }

    private static void AddPythonDeclarationScope(
        List<(BreadcrumbItem Item, int Indent)> scopeStack,
        string line,
        int lineNumber)
    {
        foreach (var pattern in PythonPatterns)
        {
            var match = pattern.Regex.Match(line);
            if (!match.Success)
                continue;

            var indent = match.Groups[1].Value.Length;
            while (scopeStack.Count > 0 && scopeStack[^1].Indent >= indent)
                scopeStack.RemoveAt(scopeStack.Count - 1);

            scopeStack.Add((new BreadcrumbItem(match.Groups[2].Value, pattern.Kind, lineNumber), indent));
            return;
        }
    }

    private sealed class BreadcrumbBraceScanner
    {
        private readonly string _line;
        private int _index;
        private bool _inString;
        private char _stringChar;

        public BreadcrumbBraceScanner(string line)
        {
            _line = line;
        }

        public bool TryRead(out BreadcrumbBraceToken token)
        {
            while (_index < _line.Length)
            {
                var current = _line[_index];
                _index++;

                if (_inString)
                {
                    UpdateStringState(current);
                    continue;
                }

                if (StartsLineComment(current))
                    break;

                if (current == '"' || current == '\'')
                {
                    _inString = true;
                    _stringChar = current;
                    continue;
                }

                if (current == '{')
                {
                    token = BreadcrumbBraceToken.Open;
                    return true;
                }

                if (current == '}')
                {
                    token = BreadcrumbBraceToken.Close;
                    return true;
                }
            }

            token = BreadcrumbBraceToken.None;
            return false;
        }

        private void UpdateStringState(char current)
        {
            if (current == _stringChar && (_index < 2 || _line[_index - 2] != '\\'))
                _inString = false;
        }

        private bool StartsLineComment(char current) =>
            current == '/' && _index < _line.Length && _line[_index] == '/';
    }

    private enum BreadcrumbBraceToken
    {
        None,
        Open,
        Close
    }
}
