using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace FastEdit.Services;

/// <summary>
/// Computes side-by-side diffs between two text inputs.
/// </summary>
public class DiffService
{
    public DiffResult ComputeDiff(string leftText, string rightText)
    {
        var diffBuilder = new SideBySideDiffBuilder(new Differ());
        var diffModel = diffBuilder.BuildDiffModel(leftText, rightText);

        var leftLines = new List<DiffLine>();
        var rightLines = new List<DiffLine>();

        for (int i = 0; i < diffModel.OldText.Lines.Count; i++)
        {
            var line = diffModel.OldText.Lines[i];
            leftLines.Add(new DiffLine(i + 1, line.Text ?? "", line.Type));
        }

        for (int i = 0; i < diffModel.NewText.Lines.Count; i++)
        {
            var line = diffModel.NewText.Lines[i];
            rightLines.Add(new DiffLine(i + 1, line.Text ?? "", line.Type));
        }

        int changeCount = leftLines.Count(l => l.Type != ChangeType.Unchanged)
                        + rightLines.Count(l => l.Type != ChangeType.Unchanged);

        return new DiffResult(leftLines, rightLines, changeCount);
    }
}

public record DiffLine(int LineNumber, string Text, ChangeType Type);

public record DiffResult(
    IReadOnlyList<DiffLine> LeftLines,
    IReadOnlyList<DiffLine> RightLines,
    int ChangeCount)
{
    public string LeftText => string.Join("\n", LeftLines.Select(l => l.Text));
    public string RightText => string.Join("\n", RightLines.Select(l => l.Text));
    public IReadOnlyList<(int LineNum, ChangeType Type)> LeftDiffLines =>
        LeftLines.Where(l => l.Type != ChangeType.Unchanged).Select(l => (l.LineNumber, l.Type)).ToList();
    public IReadOnlyList<(int LineNum, ChangeType Type)> RightDiffLines =>
        RightLines.Where(l => l.Type != ChangeType.Unchanged).Select(l => (l.LineNumber, l.Type)).ToList();
}
