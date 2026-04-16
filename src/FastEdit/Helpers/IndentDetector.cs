namespace FastEdit.Helpers;

public static class IndentDetector
{
    public record IndentInfo(bool UseTabs, int IndentSize);

    public static IndentInfo Detect(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new IndentInfo(false, 4);

        var lines = text.Split('\n');
        int tabLines = 0;
        int spaceLines = 0;
        var indentDiffs = new Dictionary<int, int> { [2] = 0, [3] = 0, [4] = 0, [8] = 0 };
        int prevIndent = 0;
        int linesAnalyzed = 0;

        foreach (var rawLine in lines)
        {
            if (linesAnalyzed >= 100)
                break;

            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line.TrimStart().Length == 0)
                continue;

            linesAnalyzed++;

            if (line[0] == '\t')
            {
                tabLines++;
                prevIndent = 0;
            }
            else if (line[0] == ' ')
            {
                spaceLines++;
                int spaces = 0;
                while (spaces < line.Length && line[spaces] == ' ')
                    spaces++;

                int diff = Math.Abs(spaces - prevIndent);
                if (diff > 0 && indentDiffs.ContainsKey(diff))
                    indentDiffs[diff]++;

                prevIndent = spaces;
            }
            else
            {
                prevIndent = 0;
            }
        }

        if (tabLines > spaceLines)
            return new IndentInfo(true, 4);

        // Find the most common indent size
        int bestSize = 4;
        int bestCount = 0;
        foreach (var (size, count) in indentDiffs)
        {
            if (count > bestCount)
            {
                bestCount = count;
                bestSize = size;
            }
        }

        return new IndentInfo(false, bestSize);
    }
}
