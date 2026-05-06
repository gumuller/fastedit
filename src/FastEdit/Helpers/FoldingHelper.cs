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
            xmlStrategy.UpdateFoldings(manager, document);
        else if (strategy is BraceFoldingStrategy braceStrategy)
            braceStrategy.UpdateFoldings(manager, document);
        else if (strategy is IndentFoldingStrategy indentStrategy)
            indentStrategy.UpdateFoldings(manager, document);
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
        manager.UpdateFoldings(BraceFoldingBuilder.Create(document), -1);
    }
}

public static class BraceFoldingBuilder
{
    public static IEnumerable<NewFolding> Create(TextDocument document)
    {
        var foldings = new List<NewFolding>();
        var stack = new Stack<int>();
        var scanner = new BraceTokenScanner(document);

        for (int i = 0; i < document.TextLength; i++)
        {
            switch (scanner.Read(ref i))
            {
                case BraceToken.Open:
                    stack.Push(i);
                    break;
                case BraceToken.Close when stack.Count > 0:
                    AddFoldIfMultiline(foldings, document, stack.Pop(), i);
                    break;
            }
        }

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }

    private static void AddFoldIfMultiline(List<NewFolding> foldings, TextDocument document, int openOffset, int closeOffset)
    {
        if (closeOffset - openOffset <= 1)
            return;

        var startLine = document.GetLineByOffset(openOffset);
        var endLine = document.GetLineByOffset(closeOffset);
        if (endLine.LineNumber > startLine.LineNumber)
            foldings.Add(new NewFolding(openOffset, closeOffset + 1));
    }

    private sealed class BraceTokenScanner
    {
        private readonly TextDocument _document;
        private bool _inString;
        private bool _inLineComment;
        private bool _inBlockComment;
        private char _stringChar;

        public BraceTokenScanner(TextDocument document)
        {
            _document = document;
        }

        public BraceToken Read(ref int index)
        {
            var current = _document.GetCharAt(index);

            if (_inLineComment)
                return ReadLineComment(current);

            if (_inBlockComment)
                return ReadBlockComment(current, ref index);

            if (_inString)
                return ReadString(current, ref index);

            if (current == '"' || current == '\'')
            {
                _inString = true;
                _stringChar = current;
                return BraceToken.None;
            }

            if (current == '/' && index + 1 < _document.TextLength)
                return ReadCommentStart(ref index);

            return current switch
            {
                '{' => BraceToken.Open,
                '}' => BraceToken.Close,
                _ => BraceToken.None
            };
        }

        private BraceToken ReadLineComment(char current)
        {
            if (current == '\n')
                _inLineComment = false;

            return BraceToken.None;
        }

        private BraceToken ReadBlockComment(char current, ref int index)
        {
            if (current == '*' && index + 1 < _document.TextLength && _document.GetCharAt(index + 1) == '/')
            {
                _inBlockComment = false;
                index++;
            }

            return BraceToken.None;
        }

        private BraceToken ReadString(char current, ref int index)
        {
            if (current == '\\')
                index++;
            else if (current == _stringChar)
                _inString = false;

            return BraceToken.None;
        }

        private BraceToken ReadCommentStart(ref int index)
        {
            var next = _document.GetCharAt(index + 1);
            if (next == '/')
            {
                _inLineComment = true;
                return BraceToken.None;
            }

            if (next == '*')
            {
                _inBlockComment = true;
                index++;
            }

            return BraceToken.None;
        }
    }

    private enum BraceToken
    {
        None,
        Open,
        Close
    }
}

public class IndentFoldingStrategy : IFoldingStrategy
{
    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        manager.UpdateFoldings(IndentFoldingBuilder.Create(document), -1);
    }
}

public static class IndentFoldingBuilder
{
    public static IEnumerable<NewFolding> Create(TextDocument document)
    {
        var foldings = new List<NewFolding>();
        var stack = new Stack<(int indent, int startOffset)>();

        for (int lineNum = 1; lineNum <= document.LineCount; lineNum++)
        {
            var line = document.GetLineByNumber(lineNum);
            var text = document.GetText(line.Offset, line.Length);

            if (string.IsNullOrWhiteSpace(text)) continue;

            var indent = CountIndent(text);
            CloseCompletedScopes(foldings, document, stack, indent, lineNum);

            if (IsFoldHeader(text))
                stack.Push((indent, line.Offset));
        }

        CloseRemainingScopes(foldings, document, stack);

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }

    private static int CountIndent(string text)
    {
        var indent = 0;
        foreach (var c in text)
        {
            if (c == ' ') indent++;
            else if (c == '\t') indent += 4;
            else break;
        }

        return indent;
    }

    private static void CloseCompletedScopes(
        List<NewFolding> foldings,
        TextDocument document,
        Stack<(int indent, int startOffset)> stack,
        int indent,
        int lineNumber)
    {
        while (stack.Count > 0 && stack.Peek().indent >= indent)
            AddCompletedScope(foldings, document, stack.Pop().startOffset, lineNumber - 1);
    }

    private static void AddCompletedScope(List<NewFolding> foldings, TextDocument document, int startOffset, int endLineNumber)
    {
        var startLine = document.GetLineByOffset(startOffset);
        if (endLineNumber > startLine.LineNumber)
            foldings.Add(new NewFolding(startOffset, document.GetLineByNumber(endLineNumber).EndOffset));
    }

    private static bool IsFoldHeader(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.EndsWith(':') || trimmed.EndsWith("def") || trimmed.EndsWith("class");
    }

    private static void CloseRemainingScopes(
        List<NewFolding> foldings,
        TextDocument document,
        Stack<(int indent, int startOffset)> stack)
    {
        while (stack.Count > 0)
            AddCompletedScope(foldings, document, stack.Pop().startOffset, document.LineCount);
    }
}

// Wrap AvalonEdit's XmlFoldingStrategy to implement our interface
public class XmlFoldingStrategy : ICSharpCode.AvalonEdit.Folding.XmlFoldingStrategy, IFoldingStrategy
{
}
