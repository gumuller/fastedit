using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public static class TextToolOperationRunner
{
    public static TextToolResult Execute(ITextToolsService textTools, TextToolOperation operation, string input) =>
        operation switch
        {
            TextToolOperation.UpperCase => textTools.ToUpperCase(input),
            TextToolOperation.LowerCase => textTools.ToLowerCase(input),
            TextToolOperation.TitleCase => textTools.ToTitleCase(input),
            TextToolOperation.InvertCase => textTools.InvertCase(input),
            TextToolOperation.RemoveDuplicateLines => textTools.RemoveDuplicateLines(input),
            TextToolOperation.SortLinesAsc => textTools.SortLinesAscending(input),
            TextToolOperation.SortLinesDesc => textTools.SortLinesDescending(input),
            TextToolOperation.TrimTrailing => textTools.TrimTrailingWhitespace(input),
            TextToolOperation.TrimLeading => textTools.TrimLeadingWhitespace(input),
            TextToolOperation.TrimAll => textTools.TrimAllWhitespace(input),
            TextToolOperation.TabsToSpaces => textTools.TabsToSpaces(input),
            TextToolOperation.SpacesToTabs => textTools.SpacesToTabs(input),
            TextToolOperation.Base64Encode => textTools.Base64Encode(input),
            TextToolOperation.Base64Decode => textTools.Base64Decode(input),
            TextToolOperation.UrlEncode => textTools.UrlEncode(input),
            TextToolOperation.UrlDecode => textTools.UrlDecode(input),
            TextToolOperation.ComputeMd5 => textTools.ComputeMd5(input),
            TextToolOperation.ComputeSha1 => textTools.ComputeSha1(input),
            TextToolOperation.ComputeSha256 => textTools.ComputeSha256(input),
            TextToolOperation.ComputeSha512 => textTools.ComputeSha512(input),
            _ => TextToolResult.Fail($"Unknown text tool: {operation}")
        };

    public static bool IsChecksum(this TextToolOperation operation) =>
        operation is TextToolOperation.ComputeMd5
            or TextToolOperation.ComputeSha1
            or TextToolOperation.ComputeSha256
            or TextToolOperation.ComputeSha512;

    public static bool TryParseLegacyName(string operationName, out TextToolOperation operation)
    {
        if (Enum.TryParse(operationName, true, out operation))
        {
            return true;
        }

        operation = operationName.ToUpperInvariant() switch
        {
            "MD5" => TextToolOperation.ComputeMd5,
            "SHA1" => TextToolOperation.ComputeSha1,
            "SHA256" => TextToolOperation.ComputeSha256,
            "SHA512" => TextToolOperation.ComputeSha512,
            _ => operation
        };

        return operation is TextToolOperation.ComputeMd5
            or TextToolOperation.ComputeSha1
            or TextToolOperation.ComputeSha256
            or TextToolOperation.ComputeSha512;
    }
}
