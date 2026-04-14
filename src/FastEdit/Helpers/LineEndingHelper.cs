namespace FastEdit.Helpers;

public enum LineEndingType
{
    CRLF,   // Windows \r\n
    LF,     // Unix \n
    CR,     // Old Mac \r
    Mixed
}

public static class LineEndingHelper
{
    public static LineEndingType Detect(string text)
    {
        if (string.IsNullOrEmpty(text)) return LineEndingType.CRLF;

        bool hasCRLF = false, hasLF = false, hasCR = false;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    hasCRLF = true;
                    i++; // skip the \n
                }
                else
                {
                    hasCR = true;
                }
            }
            else if (text[i] == '\n')
            {
                hasLF = true;
            }
        }

        int types = (hasCRLF ? 1 : 0) + (hasLF ? 1 : 0) + (hasCR ? 1 : 0);
        if (types > 1) return LineEndingType.Mixed;
        if (hasLF) return LineEndingType.LF;
        if (hasCR) return LineEndingType.CR;
        return LineEndingType.CRLF;
    }

    public static string Convert(string text, LineEndingType target)
    {
        // First normalize to \n
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");

        return target switch
        {
            LineEndingType.CRLF => normalized.Replace("\n", "\r\n"),
            LineEndingType.LF => normalized,
            LineEndingType.CR => normalized.Replace("\n", "\r"),
            _ => normalized.Replace("\n", "\r\n")
        };
    }

    public static string ToDisplayString(LineEndingType type) => type switch
    {
        LineEndingType.CRLF => "CRLF",
        LineEndingType.LF => "LF",
        LineEndingType.CR => "CR",
        LineEndingType.Mixed => "Mixed",
        _ => "CRLF"
    };
}
