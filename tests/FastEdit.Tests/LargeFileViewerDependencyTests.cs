using FastEdit.Services.Interfaces;
using FastEdit.Views.Controls;

namespace FastEdit.Tests;

public class LargeFileViewerDependencyTests
{
    [Fact]
    public void FilterService_IsBindableDependencyProperty()
    {
        Assert.Equal(typeof(ILineFilterService), LargeFileViewer.FilterServiceProperty.PropertyType);
        Assert.Equal(nameof(LargeFileViewer.FilterService), LargeFileViewer.FilterServiceProperty.Name);
        Assert.Equal(typeof(LargeFileViewer), LargeFileViewer.FilterServiceProperty.OwnerType);
    }
}
