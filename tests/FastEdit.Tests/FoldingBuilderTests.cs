using FastEdit.Helpers;
using FastEdit.Models;
using ICSharpCode.AvalonEdit.Document;

namespace FastEdit.Tests;

public class FoldingBuilderTests
{
    [Fact]
    public void FilterFoldingBuilder_CreatesHiddenRangesForNonVisibleLines()
    {
        var document = new TextDocument("hidden 1\nvisible\nhidden 2\nhidden 3\nvisible");
        var visibleFilter = new LineFilter { Pattern = "visible" };
        var results = new Dictionary<int, LineFilterResult>
        {
            [2] = new(true, false, visibleFilter),
            [5] = new(true, false, visibleFilter)
        };

        var folds = FilterFoldingBuilder.Create(document, results);

        Assert.Equal(2, folds.Count);
        Assert.Equal("[1 hidden line]", folds[0].Name);
        Assert.Equal("[2 hidden lines]", folds[1].Name);
        Assert.All(folds, fold => Assert.True(fold.DefaultClosed));
    }

    [Fact]
    public void BraceFoldingBuilder_IgnoresBracesInsideStringsAndComments()
    {
        var document = new TextDocument("""
            class Sample {
                string text = "{";
                // ignored {
                void Run() {
                }
            }
            """);

        var folds = BraceFoldingBuilder.Create(document).ToList();

        Assert.Equal(2, folds.Count);
    }

    [Fact]
    public void IndentFoldingBuilder_CreatesNestedPythonFolds()
    {
        var document = new TextDocument("""
            class Sample:
                def run(self):
                    print("hi")
                def stop(self):
                    print("bye")
            outside = True
            """);

        var folds = IndentFoldingBuilder.Create(document).ToList();

        Assert.Equal(3, folds.Count);
        Assert.True(folds[0].StartOffset < folds[1].StartOffset);
        Assert.True(folds[1].StartOffset < folds[2].StartOffset);
    }
}
