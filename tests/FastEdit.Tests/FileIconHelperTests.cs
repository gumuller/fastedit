using FastEdit.Helpers;

namespace FastEdit.Tests;

public class FileIconHelperTests
{
    [Fact]
    public void GetIcon_Directory_ReturnsFolderIcon()
    {
        Assert.Equal("\uE8B7", FileIconHelper.GetIcon("folder", isDirectory: true));
    }

    [Theory]
    [InlineData("code.cs", "\uE943")]
    [InlineData("page.html", "\uE774")]
    [InlineData("style.css", "\uE790")]
    [InlineData("data.json", "\uE9D5")]
    [InlineData("notes.md", "\uE8A5")]
    [InlineData("image.png", "\uEB9F")]
    [InlineData("archive.zip", "\uF012")]
    [InlineData("script.ps1", "\uE756")]
    [InlineData("unknown.bin", "\uE8A5")]
    public void GetIcon_UsesExtensionMap(string fileName, string expectedIcon)
    {
        Assert.Equal(expectedIcon, FileIconHelper.GetIcon(fileName));
    }

    [Theory]
    [InlineData("code.cs", "#68217A")]
    [InlineData("component.tsx", "#3178C6")]
    [InlineData("page.htm", "#E44D26")]
    [InlineData("workflow.yml", "#CB171E")]
    [InlineData("unknown.bin", null)]
    public void GetIconColor_UsesExtensionMap(string fileName, string? expectedColor)
    {
        Assert.Equal(expectedColor, FileIconHelper.GetIconColor(fileName));
    }
}
