using System.Text.RegularExpressions;

namespace FastEdit.Infrastructure;

public static class LineFilterInputValidator
{
    public static LineFilterValidationResult Validate(string? patternInput, bool isRegex)
    {
        var pattern = patternInput?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(pattern))
            return LineFilterValidationResult.Invalid("Pattern cannot be empty.");

        if (isRegex)
        {
            try
            {
                _ = new Regex(pattern);
            }
            catch (ArgumentException ex)
            {
                return LineFilterValidationResult.Invalid($"Invalid regex: {ex.Message}");
            }
        }

        return LineFilterValidationResult.Valid(pattern);
    }
}

public sealed record LineFilterValidationResult(bool IsValid, string Pattern, string? ErrorMessage)
{
    public static LineFilterValidationResult Valid(string pattern) => new(true, pattern, null);

    public static LineFilterValidationResult Invalid(string errorMessage) => new(false, string.Empty, errorMessage);
}
