using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using FastEdit.Core.LargeFile;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;
using FastEdit.Views;
using FastEdit.Views.Controls;
using Moq;

namespace FastEdit.Tests;

public class LargeFileViewerStateTests
{
    [Fact]
    public async Task DataContextSwitch_CapturesAndRestoresPhysicalTopLine()
    {
        var firstPath = Path.GetTempFileName();
        var secondPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(
                firstPath,
                Enumerable.Range(1, 120).Select(index => $"first {index}"));
            await File.WriteAllLinesAsync(
                secondPath,
                Enumerable.Range(1, 120).Select(index => $"second {index}"));
            using var firstDocument = new LargeFileDocument(firstPath);
            using var secondDocument = new LargeFileDocument(secondPath);
            await firstDocument.BuildIndexAsync(null, CancellationToken.None);
            await secondDocument.BuildIndexAsync(null, CancellationToken.None);

            await WpfTestHost.RunAsync(() =>
            {
                var first = CreateTab(firstDocument);
                var second = CreateTab(secondDocument);
                second.LargeFileTopLine = 70;
                var viewer = new LargeFileViewer { DataContext = first };
                var root = new Grid();
                root.Children.Add(viewer);

                viewer.GoToLine(40);
                viewer.DataContext = second;

                Assert.Equal(40, first.LargeFileTopLine);
                MainWindow.CaptureEditorStates(root);
                Assert.Equal(70, second.LargeFileTopLine);

                viewer.DataContext = first;
                MainWindow.CaptureEditorStates(root);
                Assert.Equal(40, first.LargeFileTopLine);
                return Task.CompletedTask;
            });
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    private static EditorTabViewModel CreateTab(LargeFileDocument document)
    {
        var tab = new EditorTabViewModel(
            Mock.Of<IFileService>(),
            Mock.Of<IFileSystemService>(),
            Mock.Of<IDialogService>());
        typeof(EditorTabViewModel)
            .GetField("_largeFileDoc", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(tab, document);
        return tab;
    }
}
