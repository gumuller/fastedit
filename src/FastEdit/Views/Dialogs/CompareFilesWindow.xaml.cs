using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DiffPlex.DiffBuilder.Model;
using FastEdit.Services;
using FastEdit.Services.Interfaces;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace FastEdit.Views.Dialogs;

public partial class CompareFilesWindow : Window
{
    private readonly DiffService _diffService;
    private readonly IFileSystemService _fileSystemService;
    private bool _isSyncingScroll;

    public CompareFilesWindow(IFileSystemService fileSystemService)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _diffService = new DiffService();

        InitializeComponent();
    }

    public void CompareFiles(string leftPath, string rightPath)
    {
        LeftLabel.Text = Path.GetFileName(leftPath);
        RightLabel.Text = Path.GetFileName(rightPath);

        string leftText, rightText;
        try
        {
            leftText = _fileSystemService.ReadAllText(leftPath);
            rightText = _fileSystemService.ReadAllText(rightPath);
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

        var result = _diffService.ComputeDiff(leftText, rightText);

        LeftEditor.Document = new TextDocument(result.LeftText);
        RightEditor.Document = new TextDocument(result.RightText);

        LeftEditor.TextArea.TextView.BackgroundRenderers.Add(
            new DiffBackgroundRenderer(result.LeftDiffLines));
        RightEditor.TextArea.TextView.BackgroundRenderers.Add(
            new DiffBackgroundRenderer(result.RightDiffLines));

        DiffStatus.Text = $"{result.ChangeCount} difference(s) found";

        // Sync scrolling — flag is cleared via Dispatcher so it persists through
        // the async layout pass that fires the cascading ScrollOffsetChanged event.
        LeftEditor.ScrollToHome();
        RightEditor.ScrollToHome();

        LeftEditor.TextArea.TextView.ScrollOffsetChanged += (s, e) =>
        {
            if (_isSyncingScroll) return;
            _isSyncingScroll = true;
            RightEditor.ScrollToVerticalOffset(LeftEditor.TextArea.TextView.VerticalOffset);
            RightEditor.ScrollToHorizontalOffset(LeftEditor.TextArea.TextView.HorizontalOffset);
            Dispatcher.BeginInvoke(() => _isSyncingScroll = false, DispatcherPriority.Background);
        };
        RightEditor.TextArea.TextView.ScrollOffsetChanged += (s, e) =>
        {
            if (_isSyncingScroll) return;
            _isSyncingScroll = true;
            LeftEditor.ScrollToVerticalOffset(RightEditor.TextArea.TextView.VerticalOffset);
            LeftEditor.ScrollToHorizontalOffset(RightEditor.TextArea.TextView.HorizontalOffset);
            Dispatcher.BeginInvoke(() => _isSyncingScroll = false, DispatcherPriority.Background);
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

    public DiffBackgroundRenderer(IReadOnlyList<(int LineNum, ChangeType Type)> diffLines)
    {
        _diffLines = diffLines.ToDictionary(d => d.LineNum, d => d.Type);
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
