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
            if (string.IsNullOrWhiteSpace(line))
                continue;

            linesAnalyzed++;

            var indentation = AnalyzeLineIndent(line, prevIndent, indentDiffs);
            tabLines += indentation.TabLineCount;
            spaceLines += indentation.SpaceLineCount;
            prevIndent = indentation.CurrentIndent;
        }

        if (tabLines > spaceLines)
            return new IndentInfo(true, 4);

        return new IndentInfo(false, FindMostCommonIndentSize(indentDiffs));
    }

    private static LineIndentAnalysis AnalyzeLineIndent(
        string line,
        int previousIndent,
        Dictionary<int, int> indentDiffs)
    {
        if (line[0] == '\t')
            return new LineIndentAnalysis(TabLineCount: 1, SpaceLineCount: 0, CurrentIndent: 0);

        if (line[0] != ' ')
            return new LineIndentAnalysis(TabLineCount: 0, SpaceLineCount: 0, CurrentIndent: 0);

        var spaces = CountLeadingSpaces(line);
        var diff = Math.Abs(spaces - previousIndent);
        if (diff > 0 && indentDiffs.ContainsKey(diff))
            indentDiffs[diff]++;

        return new LineIndentAnalysis(TabLineCount: 0, SpaceLineCount: 1, CurrentIndent: spaces);
    }

    private static int CountLeadingSpaces(string line)
    {
        var spaces = 0;
        while (spaces < line.Length && line[spaces] == ' ')
            spaces++;
        return spaces;
    }

    private static int FindMostCommonIndentSize(IReadOnlyDictionary<int, int> indentDiffs)
    {
        var bestSize = 4;
        var bestCount = 0;
        foreach (var (size, count) in indentDiffs)
        {
            if (count <= bestCount)
                continue;

            bestCount = count;
            bestSize = size;
        }

        return bestSize;
    }

    private readonly record struct LineIndentAnalysis(int TabLineCount, int SpaceLineCount, int CurrentIndent);
}
