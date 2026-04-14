using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace FastEdit.Helpers;

public static class FoldingHelper
{
    public static FoldingManager? Install(TextEditor editor, string language)
    {
        // Uninstall any existing FoldingManager first
        var existing = editor.TextArea.TextView.Services.GetService(typeof(FoldingManager)) as FoldingManager;
        if (existing != null)
            FoldingManager.Uninstall(existing);

        var strategy = GetStrategy(language);
        if (strategy == null) return null;

        var manager = FoldingManager.Install(editor.TextArea);
        UpdateFoldings(manager, strategy, editor.Document);
        return manager;
    }

    public static void Update(FoldingManager? manager, string language, TextDocument document)
    {
        if (manager == null) return;
        var strategy = GetStrategy(language);
        if (strategy == null) return;
        UpdateFoldings(manager, strategy, document);
    }

    public static void Uninstall(TextEditor editor)
    {
        // Get existing FoldingManagers from the text area
        foreach (var manager in editor.TextArea.TextView.Services.GetService(typeof(FoldingManager)) is FoldingManager fm
            ? new[] { fm } : Array.Empty<FoldingManager>())
        {
            FoldingManager.Uninstall(manager);
        }
    }

    private static void UpdateFoldings(FoldingManager manager, IFoldingStrategy strategy, TextDocument document)
    {
        if (strategy is XmlFoldingStrategy xmlStrategy)
        {
            xmlStrategy.UpdateFoldings(manager, document);
        }
        else if (strategy is BraceFoldingStrategy braceStrategy)
        {
            braceStrategy.UpdateFoldings(manager, document);
        }
    }

    private static IFoldingStrategy? GetStrategy(string language)
    {
        return language switch
        {
            "XML" or "HTML" => new XmlFoldingStrategy(),
            "C#" or "JavaScript" or "TypeScript" or "Java" or "C++" or "C" or "Go" or "Rust" or "CSS" or "JSON" =>
                new BraceFoldingStrategy(),
            "Python" => new IndentFoldingStrategy(),
            _ => null
        };
    }
}

// Marker interface for strategies
public interface IFoldingStrategy { }

public class BraceFoldingStrategy : IFoldingStrategy
{
    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        var foldings = CreateNewFoldings(document);
        manager.UpdateFoldings(foldings, -1);
    }

    private static IEnumerable<NewFolding> CreateNewFoldings(TextDocument document)
    {
        var foldings = new List<NewFolding>();
        var stack = new Stack<int>();
        bool inString = false;
        bool inLineComment = false;
        bool inBlockComment = false;
        char stringChar = '\0';

        for (int i = 0; i < document.TextLength; i++)
        {
            char c = document.GetCharAt(i);

            if (inLineComment)
            {
                if (c == '\n') inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && i + 1 < document.TextLength && document.GetCharAt(i + 1) == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }

            if (inString)
            {
                if (c == '\\') { i++; continue; }
                if (c == stringChar) inString = false;
                continue;
            }

            if (c == '"' || c == '\'')
            {
                inString = true;
                stringChar = c;
                continue;
            }

            if (c == '/' && i + 1 < document.TextLength)
            {
                var next = document.GetCharAt(i + 1);
                if (next == '/') { inLineComment = true; continue; }
                if (next == '*') { inBlockComment = true; i++; continue; }
            }

            if (c == '{')
            {
                stack.Push(i);
            }
            else if (c == '}' && stack.Count > 0)
            {
                int openOffset = stack.Pop();
                if (i - openOffset > 1)
                {
                    var line = document.GetLineByOffset(openOffset);
                    var endLine = document.GetLineByOffset(i);
                    if (endLine.LineNumber > line.LineNumber)
                    {
                        foldings.Add(new NewFolding(openOffset, i + 1));
                    }
                }
            }
        }

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }
}

public class IndentFoldingStrategy : IFoldingStrategy
{
    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        var foldings = CreateNewFoldings(document);
        manager.UpdateFoldings(foldings, -1);
    }

    private static IEnumerable<NewFolding> CreateNewFoldings(TextDocument document)
    {
        var foldings = new List<NewFolding>();
        var stack = new Stack<(int indent, int startOffset)>();

        for (int lineNum = 1; lineNum <= document.LineCount; lineNum++)
        {
            var line = document.GetLineByNumber(lineNum);
            var text = document.GetText(line.Offset, line.Length);

            if (string.IsNullOrWhiteSpace(text)) continue;

            int indent = 0;
            foreach (char c in text)
            {
                if (c == ' ') indent++;
                else if (c == '\t') indent += 4;
                else break;
            }

            while (stack.Count > 0 && stack.Peek().indent >= indent)
            {
                var (_, startOffset) = stack.Pop();
                var prevLine = document.GetLineByOffset(startOffset);
                if (lineNum - prevLine.LineNumber > 1)
                {
                    var prevLineObj = document.GetLineByNumber(lineNum - 1);
                    foldings.Add(new NewFolding(startOffset, prevLineObj.EndOffset));
                }
            }

            if (text.TrimEnd().EndsWith(':') || text.TrimEnd().EndsWith("def") || text.TrimEnd().EndsWith("class"))
            {
                stack.Push((indent, line.Offset));
            }
        }

        while (stack.Count > 0)
        {
            var (_, startOffset) = stack.Pop();
            var startLine = document.GetLineByOffset(startOffset);
            if (document.LineCount > startLine.LineNumber)
            {
                foldings.Add(new NewFolding(startOffset, document.GetLineByNumber(document.LineCount).EndOffset));
            }
        }

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }
}

// Wrap AvalonEdit's XmlFoldingStrategy to implement our interface
public class XmlFoldingStrategy : ICSharpCode.AvalonEdit.Folding.XmlFoldingStrategy, IFoldingStrategy
{
}
