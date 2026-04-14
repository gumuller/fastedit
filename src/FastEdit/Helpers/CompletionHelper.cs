using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace FastEdit.Helpers;

public static class CompletionHelper
{
    public static IList<ICompletionData> GetCompletions(string language, TextDocument document, int offset)
    {
        var results = new List<ICompletionData>();

        // Get the partial word being typed
        var wordStart = FindWordStart(document, offset);
        if (wordStart >= offset) return results;

        var prefix = document.GetText(wordStart, offset - wordStart);
        if (prefix.Length < 2) return results;

        // Add language keywords
        var keywords = GetKeywords(language);
        foreach (var kw in keywords)
        {
            if (kw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && kw != prefix)
                results.Add(new CompletionItem(kw, "keyword"));
        }

        // Add document words
        var docWords = ExtractDocumentWords(document.Text, prefix);
        foreach (var word in docWords)
        {
            if (!results.Any(r => r.Text == word))
                results.Add(new CompletionItem(word, "document"));
        }

        return results;
    }

    private static int FindWordStart(TextDocument document, int offset)
    {
        int start = offset;
        while (start > 0)
        {
            char ch = document.GetCharAt(start - 1);
            if (char.IsLetterOrDigit(ch) || ch == '_')
                start--;
            else
                break;
        }
        return start;
    }

    private static HashSet<string> ExtractDocumentWords(string text, string prefix)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (Match m in Regex.Matches(text, @"\b[a-zA-Z_]\w{2,}\b", RegexOptions.None, TimeSpan.FromMilliseconds(200)))
            {
                if (m.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && m.Value != prefix)
                    words.Add(m.Value);
            }
        }
        catch (RegexMatchTimeoutException) { }

        return words;
    }

    private static string[] GetKeywords(string language) => language switch
    {
        "C#" => CSharpKeywords,
        "JavaScript" or "TypeScript" => JavaScriptKeywords,
        "Python" => PythonKeywords,
        "Java" => JavaKeywords,
        "C++" or "C" => CppKeywords,
        "Rust" => RustKeywords,
        "Go" => GoKeywords,
        "SQL" => SqlKeywords,
        "PowerShell" => PowerShellKeywords,
        _ => []
    };

    private static readonly string[] CSharpKeywords =
    [
        "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch",
        "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double",
        "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "partial",
        "private", "protected", "public", "readonly", "record", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
        "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "var", "virtual", "void",
        "volatile", "when", "where", "while", "yield"
    ];

    private static readonly string[] JavaScriptKeywords =
    [
        "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default",
        "delete", "do", "else", "export", "extends", "false", "finally", "for", "from", "function",
        "if", "import", "in", "instanceof", "let", "new", "null", "of", "return", "super",
        "switch", "this", "throw", "true", "try", "typeof", "undefined", "var", "void", "while",
        "with", "yield", "console", "document", "window", "require", "module", "Promise", "Array", "Object",
        "Map", "Set", "Symbol", "Number", "String", "Boolean", "Error", "JSON", "Math", "Date"
    ];

    private static readonly string[] PythonKeywords =
    [
        "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class",
        "continue", "def", "del", "elif", "else", "except", "finally", "for", "from", "global",
        "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise",
        "return", "try", "while", "with", "yield", "print", "range", "len", "list", "dict",
        "tuple", "set", "str", "int", "float", "bool", "type", "isinstance", "enumerate", "zip",
        "map", "filter", "sorted", "reversed", "super", "property", "staticmethod", "classmethod", "self"
    ];

    private static readonly string[] JavaKeywords =
    [
        "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class", "const",
        "continue", "default", "do", "double", "else", "enum", "extends", "final", "finally", "float",
        "for", "goto", "if", "implements", "import", "instanceof", "int", "interface", "long", "native",
        "new", "null", "package", "private", "protected", "public", "return", "short", "static", "strictfp",
        "super", "switch", "synchronized", "this", "throw", "throws", "transient", "try", "void", "volatile", "while"
    ];

    private static readonly string[] CppKeywords =
    [
        "alignas", "alignof", "and", "auto", "bool", "break", "case", "catch", "char", "class",
        "const", "constexpr", "continue", "decltype", "default", "delete", "do", "double", "else", "enum",
        "explicit", "extern", "false", "float", "for", "friend", "goto", "if", "include", "inline",
        "int", "long", "namespace", "new", "noexcept", "nullptr", "operator", "private", "protected", "public",
        "return", "short", "signed", "sizeof", "static", "static_cast", "struct", "switch", "template", "this",
        "throw", "true", "try", "typedef", "typeid", "typename", "union", "unsigned", "using", "virtual",
        "void", "volatile", "while"
    ];

    private static readonly string[] RustKeywords =
    [
        "as", "async", "await", "break", "const", "continue", "crate", "dyn", "else", "enum",
        "extern", "false", "fn", "for", "if", "impl", "in", "let", "loop", "match",
        "mod", "move", "mut", "pub", "ref", "return", "self", "Self", "static", "struct",
        "super", "trait", "true", "type", "unsafe", "use", "where", "while", "yield",
        "Box", "Option", "Result", "Some", "None", "Ok", "Err", "Vec", "String", "HashMap",
        "println", "eprintln", "format", "vec", "unwrap", "expect", "clone", "iter", "collect"
    ];

    private static readonly string[] GoKeywords =
    [
        "break", "case", "chan", "const", "continue", "default", "defer", "else", "fallthrough", "for",
        "func", "go", "goto", "if", "import", "interface", "map", "package", "range", "return",
        "select", "struct", "switch", "type", "var", "append", "cap", "close", "copy", "delete",
        "error", "false", "fmt", "len", "make", "new", "nil", "panic", "print", "println",
        "recover", "true", "string", "int", "float64", "bool", "byte", "rune"
    ];

    private static readonly string[] SqlKeywords =
    [
        "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "IN", "BETWEEN", "LIKE", "IS",
        "NULL", "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE", "CREATE", "TABLE", "ALTER",
        "DROP", "INDEX", "VIEW", "JOIN", "INNER", "LEFT", "RIGHT", "OUTER", "ON", "GROUP",
        "BY", "HAVING", "ORDER", "ASC", "DESC", "LIMIT", "OFFSET", "UNION", "ALL", "DISTINCT",
        "COUNT", "SUM", "AVG", "MIN", "MAX", "AS", "EXISTS", "CASE", "WHEN", "THEN", "ELSE", "END",
        "BEGIN", "COMMIT", "ROLLBACK", "TRANSACTION", "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "CONSTRAINT"
    ];

    private static readonly string[] PowerShellKeywords =
    [
        "begin", "break", "catch", "class", "continue", "data", "define", "do", "dynamicparam", "else",
        "elseif", "end", "enum", "exit", "filter", "finally", "for", "foreach", "from", "function",
        "hidden", "if", "in", "param", "process", "return", "static", "switch", "throw", "trap",
        "try", "until", "using", "var", "while", "Write-Host", "Write-Output", "Get-Item", "Set-Item",
        "Get-ChildItem", "Get-Content", "Set-Content", "Invoke-Command", "Select-Object", "Where-Object",
        "ForEach-Object", "Sort-Object", "Group-Object", "Measure-Object", "New-Object", "Import-Module"
    ];
}

public class CompletionItem : ICompletionData
{
    public CompletionItem(string text, string source)
    {
        Text = text;
        _source = source;
    }

    private readonly string _source;

    public System.Windows.Media.ImageSource? Image => null;
    public string Text { get; }
    public object Content => Text;
    public object Description => _source == "keyword" ? "Language keyword" : "Document word";
    public double Priority => _source == "keyword" ? 1.0 : 0.5;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);
    }
}
