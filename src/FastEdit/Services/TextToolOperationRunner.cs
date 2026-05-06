using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public static class TextToolOperationRunner
{
    private static readonly IReadOnlyDictionary<TextToolOperation, Func<ITextToolsService, string, TextToolResult>> Operations =
        new Dictionary<TextToolOperation, Func<ITextToolsService, string, TextToolResult>>
        {
            [TextToolOperation.UpperCase] = static (tools, input) => tools.ToUpperCase(input),
            [TextToolOperation.LowerCase] = static (tools, input) => tools.ToLowerCase(input),
            [TextToolOperation.TitleCase] = static (tools, input) => tools.ToTitleCase(input),
            [TextToolOperation.InvertCase] = static (tools, input) => tools.InvertCase(input),
            [TextToolOperation.RemoveDuplicateLines] = static (tools, input) => tools.RemoveDuplicateLines(input),
            [TextToolOperation.SortLinesAsc] = static (tools, input) => tools.SortLinesAscending(input),
            [TextToolOperation.SortLinesDesc] = static (tools, input) => tools.SortLinesDescending(input),
            [TextToolOperation.TrimTrailing] = static (tools, input) => tools.TrimTrailingWhitespace(input),
            [TextToolOperation.TrimLeading] = static (tools, input) => tools.TrimLeadingWhitespace(input),
            [TextToolOperation.TrimAll] = static (tools, input) => tools.TrimAllWhitespace(input),
            [TextToolOperation.TabsToSpaces] = static (tools, input) => tools.TabsToSpaces(input),
            [TextToolOperation.SpacesToTabs] = static (tools, input) => tools.SpacesToTabs(input),
            [TextToolOperation.Base64Encode] = static (tools, input) => tools.Base64Encode(input),
            [TextToolOperation.Base64Decode] = static (tools, input) => tools.Base64Decode(input),
            [TextToolOperation.UrlEncode] = static (tools, input) => tools.UrlEncode(input),
            [TextToolOperation.UrlDecode] = static (tools, input) => tools.UrlDecode(input),
            [TextToolOperation.ComputeMd5] = static (tools, input) => tools.ComputeMd5(input),
            [TextToolOperation.ComputeSha1] = static (tools, input) => tools.ComputeSha1(input),
            [TextToolOperation.ComputeSha256] = static (tools, input) => tools.ComputeSha256(input),
            [TextToolOperation.ComputeSha512] = static (tools, input) => tools.ComputeSha512(input),
        };

    private static readonly HashSet<TextToolOperation> ChecksumOperations =
    [
        TextToolOperation.ComputeMd5,
        TextToolOperation.ComputeSha1,
        TextToolOperation.ComputeSha256,
        TextToolOperation.ComputeSha512,
    ];

    private static readonly IReadOnlyDictionary<string, TextToolOperation> LegacyChecksumNames =
        new Dictionary<string, TextToolOperation>(StringComparer.OrdinalIgnoreCase)
        {
            ["MD5"] = TextToolOperation.ComputeMd5,
            ["SHA1"] = TextToolOperation.ComputeSha1,
            ["SHA256"] = TextToolOperation.ComputeSha256,
            ["SHA512"] = TextToolOperation.ComputeSha512,
        };

    public static TextToolResult Execute(ITextToolsService textTools, TextToolOperation operation, string input) =>
        Operations.TryGetValue(operation, out var execute)
            ? execute(textTools, input)
            : TextToolResult.Fail($"Unknown text tool: {operation}");

    public static bool IsChecksum(this TextToolOperation operation) =>
        ChecksumOperations.Contains(operation);

    public static bool TryParseLegacyName(string operationName, out TextToolOperation operation)
    {
        if (Enum.TryParse(operationName, true, out operation))
        {
            return true;
        }

        return LegacyChecksumNames.TryGetValue(operationName, out operation);
    }
}
