using FastEdit.Views.Controls;

namespace FastEdit.Tests;

public class LargeFileViewerViewportTests
{
    [Fact]
    public void Configure_Clamps_TopLine_To_Last_Visible_Page()
    {
        var viewport = new LargeFileViewerViewport();

        viewport.Configure(totalLineCount: 100, visibleLineCount: 10);
        viewport.SetTopLine(100);

        Assert.Equal(91, viewport.TopLine);
        Assert.Equal(91, viewport.MaxTopLine);
    }

    [Fact]
    public void ScrollBy_Clamps_At_Document_Bounds()
    {
        var viewport = new LargeFileViewerViewport();
        viewport.Configure(totalLineCount: 25, visibleLineCount: 5);

        viewport.ScrollBy(-100);
        Assert.Equal(1, viewport.TopLine);

        viewport.ScrollBy(100);
        Assert.Equal(21, viewport.TopLine);
    }

    [Fact]
    public void GoToPhysicalLine_Uses_Physical_Line_In_Unfiltered_View()
    {
        var viewport = new LargeFileViewerViewport();
        viewport.Configure(totalLineCount: 500, visibleLineCount: 20);

        viewport.GoToPhysicalLine(250);

        Assert.Equal(250, viewport.TopLine);
        Assert.Equal(250, viewport.ResolvePhysicalLine(viewport.TopLine));
    }

    [Fact]
    public void ShowOnly_Maps_Logical_Rows_To_Physical_Lines()
    {
        var viewport = new LargeFileViewerViewport();
        viewport.Configure(totalLineCount: 100, visibleLineCount: 5);

        viewport.ShowOnly(new long[] { 3, 10, 50 });

        Assert.True(viewport.IsFiltered);
        Assert.Equal(3, viewport.EffectiveLineCount);
        Assert.Equal(3, viewport.ResolvePhysicalLine(1));
        Assert.Equal(10, viewport.ResolvePhysicalLine(2));
        Assert.Equal(50, viewport.ResolvePhysicalLine(3));
        Assert.Equal(0, viewport.ResolvePhysicalLine(4));
    }

    [Fact]
    public void GoToPhysicalLine_In_Filtered_View_Uses_Nearest_Matching_Line()
    {
        var viewport = new LargeFileViewerViewport();
        viewport.Configure(totalLineCount: 100, visibleLineCount: 1);
        viewport.ShowOnly(new long[] { 3, 10, 50 });

        viewport.GoToPhysicalLine(11);

        Assert.Equal(3, viewport.TopLine);
        Assert.Equal(50, viewport.ResolvePhysicalLine(viewport.TopLine));
    }

    [Fact]
    public void ClearShowOnly_Restores_Physical_Line_View_And_Clamps()
    {
        var viewport = new LargeFileViewerViewport();
        viewport.Configure(totalLineCount: 100, visibleLineCount: 1);
        viewport.ShowOnly(new long[] { 3, 10, 50 });
        viewport.SetTopLine(3);

        viewport.ClearShowOnly();

        Assert.False(viewport.IsFiltered);
        Assert.Equal(3, viewport.TopLine);
        Assert.Equal(100, viewport.EffectiveLineCount);
        Assert.Equal(3, viewport.ResolvePhysicalLine(3));
    }

    [Fact]
    public void Empty_Document_Keeps_Stable_Viewport()
    {
        var viewport = new LargeFileViewerViewport();

        viewport.Configure(totalLineCount: 0, visibleLineCount: 10);
        viewport.ScrollBy(10);

        Assert.Equal(1, viewport.TopLine);
        Assert.Equal(1, viewport.MaxTopLine);
        Assert.Equal(0, viewport.ResolvePhysicalLine(1));
    }
}
