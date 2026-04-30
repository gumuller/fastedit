namespace FastEdit.Infrastructure;

public static class TerminalPasteNormalizer
{
    public static string NormalizeSingleLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text
            .Replace("\r\n", " ")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
    }
}
