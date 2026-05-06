using FastEdit.Models;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace FastEdit.Helpers;

public static class FilterFoldingBuilder
{
    public static List<NewFolding> Create(
        TextDocument document,
        IReadOnlyDictionary<int, LineFilterResult> filterResults)
    {
        var folds = new List<NewFolding>();
        var hiddenRange = HiddenLineRange.None;

        for (var lineNumber = 1; lineNumber <= document.LineCount; lineNumber++)
        {
            if (!IsVisibleLine(filterResults, lineNumber))
            {
                hiddenRange = hiddenRange.StartIfNeeded(document.GetLineByNumber(lineNumber).Offset, lineNumber);
                continue;
            }

            if (hiddenRange.IsActive)
                CloseHiddenRange(folds, document, hiddenRange, lineNumber - 1);

            hiddenRange = HiddenLineRange.None;
        }

        if (hiddenRange.IsActive)
            CloseHiddenRange(folds, document, hiddenRange, document.LineCount);

        folds.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return folds;
    }

    private static bool IsVisibleLine(IReadOnlyDictionary<int, LineFilterResult> filterResults, int lineNumber) =>
        filterResults.TryGetValue(lineNumber, out var result) && result.IsVisible;

    private static void CloseHiddenRange(
        List<NewFolding> folds,
        TextDocument document,
        HiddenLineRange range,
        int endLineNumber)
    {
        var endLine = document.GetLineByNumber(endLineNumber);
        var hiddenCount = endLineNumber - range.StartLine + 1;
        folds.Add(new NewFolding(range.StartOffset, endLine.EndOffset)
        {
            Name = $"[{hiddenCount} hidden line{(hiddenCount > 1 ? "s" : "")}]",
            DefaultClosed = true
        });
    }

    private readonly record struct HiddenLineRange(int StartOffset, int StartLine)
    {
        public static HiddenLineRange None => new(-1, -1);
        public bool IsActive => StartOffset >= 0;
        public HiddenLineRange StartIfNeeded(int offset, int lineNumber) =>
            IsActive ? this : new HiddenLineRange(offset, lineNumber);
    }
}
