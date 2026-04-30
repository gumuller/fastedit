using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class ByteSizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(123L * 1024 * 1024, "123 MB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5 GB")]
    public void Format_UsesCompactBinaryUnits(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSizeFormatter.Format(bytes));
    }
}
