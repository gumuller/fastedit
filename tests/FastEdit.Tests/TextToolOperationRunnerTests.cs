using FastEdit.Services;
using FastEdit.Services.Interfaces;

namespace FastEdit.Tests;

public class TextToolOperationRunnerTests
{
    [Theory]
    [InlineData(TextToolOperation.UpperCase, nameof(ITextToolsService.ToUpperCase), false)]
    [InlineData(TextToolOperation.LowerCase, nameof(ITextToolsService.ToLowerCase), false)]
    [InlineData(TextToolOperation.TitleCase, nameof(ITextToolsService.ToTitleCase), false)]
    [InlineData(TextToolOperation.InvertCase, nameof(ITextToolsService.InvertCase), false)]
    [InlineData(TextToolOperation.RemoveDuplicateLines, nameof(ITextToolsService.RemoveDuplicateLines), false)]
    [InlineData(TextToolOperation.SortLinesAsc, nameof(ITextToolsService.SortLinesAscending), false)]
    [InlineData(TextToolOperation.SortLinesDesc, nameof(ITextToolsService.SortLinesDescending), false)]
    [InlineData(TextToolOperation.TrimTrailing, nameof(ITextToolsService.TrimTrailingWhitespace), false)]
    [InlineData(TextToolOperation.TrimLeading, nameof(ITextToolsService.TrimLeadingWhitespace), false)]
    [InlineData(TextToolOperation.TrimAll, nameof(ITextToolsService.TrimAllWhitespace), false)]
    [InlineData(TextToolOperation.TabsToSpaces, nameof(ITextToolsService.TabsToSpaces), false)]
    [InlineData(TextToolOperation.SpacesToTabs, nameof(ITextToolsService.SpacesToTabs), false)]
    [InlineData(TextToolOperation.Base64Encode, nameof(ITextToolsService.Base64Encode), false)]
    [InlineData(TextToolOperation.Base64Decode, nameof(ITextToolsService.Base64Decode), false)]
    [InlineData(TextToolOperation.UrlEncode, nameof(ITextToolsService.UrlEncode), false)]
    [InlineData(TextToolOperation.UrlDecode, nameof(ITextToolsService.UrlDecode), false)]
    [InlineData(TextToolOperation.ComputeMd5, nameof(ITextToolsService.ComputeMd5), true)]
    [InlineData(TextToolOperation.ComputeSha1, nameof(ITextToolsService.ComputeSha1), true)]
    [InlineData(TextToolOperation.ComputeSha256, nameof(ITextToolsService.ComputeSha256), true)]
    [InlineData(TextToolOperation.ComputeSha512, nameof(ITextToolsService.ComputeSha512), true)]
    public void Execute_RoutesOperationToExpectedServiceMethod(
        TextToolOperation operation,
        string expectedMethod,
        bool isChecksum)
    {
        var textTools = new TrackingTextToolsService();

        var result = TextToolOperationRunner.Execute(textTools, operation, "input");

        Assert.True(result.Success);
        Assert.Equal(expectedMethod, textTools.CalledMethod);
        Assert.Equal(expectedMethod, result.Text);
        Assert.Equal(isChecksum, operation.IsChecksum());
    }

    [Theory]
    [InlineData("MD5", TextToolOperation.ComputeMd5)]
    [InlineData("SHA1", TextToolOperation.ComputeSha1)]
    [InlineData("SHA256", TextToolOperation.ComputeSha256)]
    [InlineData("SHA512", TextToolOperation.ComputeSha512)]
    [InlineData("UpperCase", TextToolOperation.UpperCase)]
    public void TryParseLegacyName_MapsSupportedOperationNames(string name, TextToolOperation expected)
    {
        var success = TextToolOperationRunner.TryParseLegacyName(name, out var operation);

        Assert.True(success);
        Assert.Equal(expected, operation);
    }

    [Fact]
    public void TryParseLegacyName_RejectsUnknownOperationNames()
    {
        var success = TextToolOperationRunner.TryParseLegacyName("Unknown", out _);

        Assert.False(success);
    }

    private sealed class TrackingTextToolsService : ITextToolsService
    {
        public string? CalledMethod { get; private set; }

        public TextToolResult ToUpperCase(string input) => Call(nameof(ToUpperCase));
        public TextToolResult ToLowerCase(string input) => Call(nameof(ToLowerCase));
        public TextToolResult ToTitleCase(string input) => Call(nameof(ToTitleCase));
        public TextToolResult InvertCase(string input) => Call(nameof(InvertCase));
        public TextToolResult RemoveDuplicateLines(string input) => Call(nameof(RemoveDuplicateLines));
        public TextToolResult SortLinesAscending(string input) => Call(nameof(SortLinesAscending));
        public TextToolResult SortLinesDescending(string input) => Call(nameof(SortLinesDescending));
        public TextToolResult TrimTrailingWhitespace(string input) => Call(nameof(TrimTrailingWhitespace));
        public TextToolResult TrimLeadingWhitespace(string input) => Call(nameof(TrimLeadingWhitespace));
        public TextToolResult TrimAllWhitespace(string input) => Call(nameof(TrimAllWhitespace));
        public TextToolResult TabsToSpaces(string input, int spacesPerTab = 4) => Call(nameof(TabsToSpaces));
        public TextToolResult SpacesToTabs(string input, int spacesPerTab = 4) => Call(nameof(SpacesToTabs));
        public TextToolResult Base64Encode(string input) => Call(nameof(Base64Encode));
        public TextToolResult Base64Decode(string input) => Call(nameof(Base64Decode));
        public TextToolResult UrlEncode(string input) => Call(nameof(UrlEncode));
        public TextToolResult UrlDecode(string input) => Call(nameof(UrlDecode));
        public TextToolResult ComputeMd5(string input) => Call(nameof(ComputeMd5));
        public TextToolResult ComputeSha1(string input) => Call(nameof(ComputeSha1));
        public TextToolResult ComputeSha256(string input) => Call(nameof(ComputeSha256));
        public TextToolResult ComputeSha512(string input) => Call(nameof(ComputeSha512));

        private TextToolResult Call(string method)
        {
            CalledMethod = method;
            return TextToolResult.Ok(method);
        }
    }
}
