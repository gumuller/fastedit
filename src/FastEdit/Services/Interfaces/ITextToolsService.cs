namespace FastEdit.Services.Interfaces;

public record TextToolResult(bool Success, string Text, string? Message = null)
{
    public static TextToolResult Ok(string text, string? message = null) => new(true, text, message);
    public static TextToolResult Fail(string message) => new(false, string.Empty, message);
}

public interface ITextToolsService
{
    // Case transformations
    TextToolResult ToUpperCase(string input);
    TextToolResult ToLowerCase(string input);
    TextToolResult ToTitleCase(string input);
    TextToolResult InvertCase(string input);

    // Line operations
    TextToolResult RemoveDuplicateLines(string input);
    TextToolResult SortLinesAscending(string input);
    TextToolResult SortLinesDescending(string input);
    TextToolResult TrimTrailingWhitespace(string input);
    TextToolResult TrimLeadingWhitespace(string input);
    TextToolResult TrimAllWhitespace(string input);

    // Indentation
    TextToolResult TabsToSpaces(string input, int spacesPerTab = 4);
    TextToolResult SpacesToTabs(string input, int spacesPerTab = 4);

    // Encoding
    TextToolResult Base64Encode(string input);
    TextToolResult Base64Decode(string input);
    TextToolResult UrlEncode(string input);
    TextToolResult UrlDecode(string input);

    // Checksums
    TextToolResult ComputeMd5(string input);
    TextToolResult ComputeSha1(string input);
    TextToolResult ComputeSha256(string input);
    TextToolResult ComputeSha512(string input);
}
