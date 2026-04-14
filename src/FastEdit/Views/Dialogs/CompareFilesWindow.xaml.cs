using System.IO;
using System.Windows;
using System.Windows.Media;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace FastEdit.Views.Dialogs;

public partial class CompareFilesWindow : Window
{
    public CompareFilesWindow()
    {
        InitializeComponent();
    }

    public void CompareFiles(string leftPath, string rightPath)
    {
        LeftLabel.Text = Path.GetFileName(leftPath);
        RightLabel.Text = Path.GetFileName(rightPath);

        string leftText, rightText;
        try
        {
            leftText = File.ReadAllText(leftPath);
            rightText = File.ReadAllText(rightPath);
        }
        catch (Exception ex)
        {
            DiffStatus.Text = $"Error reading files: {ex.Message}";
            return;
        }

        CompareTexts(leftText, rightText, Path.GetFileName(leftPath), Path.GetFileName(rightPath));
    }

    public void CompareTexts(string leftText, string rightText, string leftName = "Left", string rightName = "Right")
    {
        LeftLabel.Text = leftName;
        RightLabel.Text = rightName;

        var diffBuilder = new SideBySideDiffBuilder(new Differ());
        var diffModel = diffBuilder.BuildDiffModel(leftText, rightText);

        // Build display text and collect diff line info
        var leftLines = new List<string>();
        var rightLines = new List<string>();
        var leftDiffLines = new List<(int lineNum, ChangeType type)>();
        var rightDiffLines = new List<(int lineNum, ChangeType type)>();

        for (int i = 0; i < diffModel.OldText.Lines.Count; i++)
        {
            var line = diffModel.OldText.Lines[i];
            leftLines.Add(line.Text ?? "");
            if (line.Type != ChangeType.Unchanged)
                leftDiffLines.Add((i + 1, line.Type));
        }

        for (int i = 0; i < diffModel.NewText.Lines.Count; i++)
        {
            var line = diffModel.NewText.Lines[i];
            rightLines.Add(line.Text ?? "");
            if (line.Type != ChangeType.Unchanged)
                rightDiffLines.Add((i + 1, line.Type));
        }

        LeftEditor.Document = new TextDocument(string.Join("\n", leftLines));
        RightEditor.Document = new TextDocument(string.Join("\n", rightLines));

        // Add diff highlighting
        LeftEditor.TextArea.TextView.BackgroundRenderers.Add(
            new DiffBackgroundRenderer(leftDiffLines));
        RightEditor.TextArea.TextView.BackgroundRenderers.Add(
            new DiffBackgroundRenderer(rightDiffLines));

        int changes = leftDiffLines.Count(d => d.type != ChangeType.Unchanged)
                    + rightDiffLines.Count(d => d.type != ChangeType.Unchanged);

        DiffStatus.Text = $"{changes} difference(s) found";

        // Sync scrolling
        LeftEditor.ScrollToHome();
        RightEditor.ScrollToHome();

        LeftEditor.TextArea.TextView.ScrollOffsetChanged += (s, e) =>
        {
            RightEditor.ScrollToVerticalOffset(LeftEditor.TextArea.TextView.VerticalOffset);
            RightEditor.ScrollToHorizontalOffset(LeftEditor.TextArea.TextView.HorizontalOffset);
        };
        RightEditor.TextArea.TextView.ScrollOffsetChanged += (s, e) =>
        {
            LeftEditor.ScrollToVerticalOffset(RightEditor.TextArea.TextView.VerticalOffset);
            LeftEditor.ScrollToHorizontalOffset(RightEditor.TextArea.TextView.HorizontalOffset);
        };
    }
}

internal class DiffBackgroundRenderer : IBackgroundRenderer
{
    private readonly Dictionary<int, ChangeType> _diffLines;

    private static readonly Brush DeletedBrush;
    private static readonly Brush InsertedBrush;
    private static readonly Brush ModifiedBrush;
    private static readonly Brush ImaginaryBrush;

    static DiffBackgroundRenderer()
    {
        DeletedBrush = new SolidColorBrush(Color.FromArgb(40, 255, 80, 80));
        DeletedBrush.Freeze();
        InsertedBrush = new SolidColorBrush(Color.FromArgb(40, 80, 255, 80));
        InsertedBrush.Freeze();
        ModifiedBrush = new SolidColorBrush(Color.FromArgb(40, 255, 200, 80));
        ModifiedBrush.Freeze();
        ImaginaryBrush = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128));
        ImaginaryBrush.Freeze();
    }

    public DiffBackgroundRenderer(List<(int lineNum, ChangeType type)> diffLines)
    {
        _diffLines = diffLines.ToDictionary(d => d.lineNum, d => d.type);
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        foreach (var line in textView.VisualLines)
        {
            var lineNum = line.FirstDocumentLine.LineNumber;
            if (!_diffLines.TryGetValue(lineNum, out var changeType)) continue;

            var brush = changeType switch
            {
                ChangeType.Deleted => DeletedBrush,
                ChangeType.Inserted => InsertedBrush,
                ChangeType.Modified => ModifiedBrush,
                ChangeType.Imaginary => ImaginaryBrush,
                _ => null
            };

            if (brush == null) continue;

            var rect = BackgroundGeometryBuilder.GetRectsFromVisualSegment(textView, line, 0, 1000)
                .FirstOrDefault();

            if (rect != default)
            {
                drawingContext.DrawRectangle(brush, null,
                    new Rect(0, rect.Top, textView.ActualWidth, rect.Height));
            }
        }
    }
}
