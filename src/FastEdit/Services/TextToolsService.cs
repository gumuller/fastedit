using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public class TextToolsService : ITextToolsService
{
    // Case transformations

    public TextToolResult ToUpperCase(string input) =>
        TextToolResult.Ok(input.ToUpperInvariant(), "Converted to UPPERCASE");

    public TextToolResult ToLowerCase(string input) =>
        TextToolResult.Ok(input.ToLowerInvariant(), "Converted to lowercase");

    public TextToolResult ToTitleCase(string input)
    {
        var ti = CultureInfo.CurrentCulture.TextInfo;
        return TextToolResult.Ok(ti.ToTitleCase(input.ToLower(CultureInfo.CurrentCulture)), "Converted to Title Case");
    }

    public TextToolResult InvertCase(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            sb.Append(char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c));
        }
        return TextToolResult.Ok(sb.ToString(), "Inverted case");
    }

    // Line operations

    public TextToolResult RemoveDuplicateLines(string input)
    {
        var (lines, ending) = SplitLines(input);
        var seen = new HashSet<string>();
        var unique = lines.Where(line => seen.Add(line)).ToList();
        var removed = lines.Length - unique.Count;
        return TextToolResult.Ok(
            string.Join(ending, unique),
            $"Removed {removed} duplicate line(s)");
    }

    public TextToolResult SortLinesAscending(string input)
    {
        var (lines, ending) = SplitLines(input);
        Array.Sort(lines, StringComparer.Ordinal);
        return TextToolResult.Ok(string.Join(ending, lines), "Lines sorted ascending");
    }

    public TextToolResult SortLinesDescending(string input)
    {
        var (lines, ending) = SplitLines(input);
        Array.Sort(lines, StringComparer.Ordinal);
        Array.Reverse(lines);
        return TextToolResult.Ok(string.Join(ending, lines), "Lines sorted descending");
    }

    public TextToolResult TrimTrailingWhitespace(string input)
    {
        var (lines, ending) = SplitLines(input);
        var trimmed = lines.Select(l => l.TrimEnd()).ToArray();
        return TextToolResult.Ok(string.Join(ending, trimmed), "Trimmed trailing whitespace");
    }

    public TextToolResult TrimLeadingWhitespace(string input)
    {
        var (lines, ending) = SplitLines(input);
        var trimmed = lines.Select(l => l.TrimStart()).ToArray();
        return TextToolResult.Ok(string.Join(ending, trimmed), "Trimmed leading whitespace");
    }

    public TextToolResult TrimAllWhitespace(string input)
    {
        var (lines, ending) = SplitLines(input);
        var trimmed = lines.Select(l => l.Trim()).ToArray();
        return TextToolResult.Ok(string.Join(ending, trimmed), "Trimmed all whitespace");
    }

    // Indentation

    public TextToolResult TabsToSpaces(string input, int spacesPerTab = 4)
    {
        var spaces = new string(' ', spacesPerTab);
        return TextToolResult.Ok(input.Replace("\t", spaces), $"Converted tabs to {spacesPerTab} spaces");
    }

    public TextToolResult SpacesToTabs(string input, int spacesPerTab = 4)
    {
        var spaces = new string(' ', spacesPerTab);
        var (lines, ending) = SplitLines(input);
        var converted = lines.Select(line => ConvertLeadingSpacesToTabs(line, spaces)).ToArray();
        return TextToolResult.Ok(string.Join(ending, converted), $"Converted leading {spacesPerTab}-space groups to tabs");
    }

    private static string ConvertLeadingSpacesToTabs(string line, string spaces)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i + spaces.Length <= line.Length && line.Substring(i, spaces.Length) == spaces)
        {
            sb.Append('\t');
            i += spaces.Length;
        }
        sb.Append(line.AsSpan(i));
        return sb.ToString();
    }

    // Encoding

    public TextToolResult Base64Encode(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return TextToolResult.Ok(Convert.ToBase64String(bytes), "Base64 encoded");
    }

    public TextToolResult Base64Decode(string input)
    {
        try
        {
            var bytes = Convert.FromBase64String(input.Trim());
            return TextToolResult.Ok(Encoding.UTF8.GetString(bytes), "Base64 decoded");
        }
        catch (FormatException)
        {
            return TextToolResult.Fail("Invalid Base64 input");
        }
    }

    public TextToolResult UrlEncode(string input) =>
        TextToolResult.Ok(Uri.EscapeDataString(input), "URL encoded");

    public TextToolResult UrlDecode(string input)
    {
        try
        {
            return TextToolResult.Ok(Uri.UnescapeDataString(input), "URL decoded");
        }
        catch (UriFormatException)
        {
            return TextToolResult.Fail("Invalid URL-encoded input");
        }
    }

    // Checksums

    public TextToolResult ComputeMd5(string input) =>
        ComputeHash(input, MD5.Create(), "MD5");

    public TextToolResult ComputeSha1(string input) =>
        ComputeHash(input, SHA1.Create(), "SHA-1");

    public TextToolResult ComputeSha256(string input) =>
        ComputeHash(input, SHA256.Create(), "SHA-256");

    public TextToolResult ComputeSha512(string input) =>
        ComputeHash(input, SHA512.Create(), "SHA-512");

    private static TextToolResult ComputeHash(string input, HashAlgorithm algorithm, string name)
    {
        using (algorithm)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = algorithm.ComputeHash(bytes);
            var hex = Convert.ToHexString(hash).ToLowerInvariant();
            return TextToolResult.Ok(hex, $"{name}: {hex}");
        }
    }

    // Helpers

    private static (string[] Lines, string Ending) SplitLines(string input)
    {
        var ending = input.Contains("\r\n") ? "\r\n" : "\n";
        var lines = input.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return (lines, ending);
    }
}
