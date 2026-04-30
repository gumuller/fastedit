using System.IO;

namespace FastEdit.Infrastructure;

public static class TerminalPromptFormatter
{
    public static string FormatPrompt(string workingDirectory, string userProfile)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
            return "❯ ";

        var displayPath = workingDirectory;
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            if (workingDirectory.Equals(userProfile, StringComparison.OrdinalIgnoreCase))
            {
                displayPath = "~";
            }
            else if (workingDirectory.StartsWith(userProfile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                displayPath = "~" + workingDirectory[userProfile.Length..];
            }
        }

        return $"{displayPath} ❯ ";
    }
}
