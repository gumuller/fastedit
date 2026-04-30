namespace FastEdit.Infrastructure;

public static class GoToLineInputParser
{
    public static bool TryParse(string? input, out int lineNumber)
    {
        return int.TryParse(input, out lineNumber) && lineNumber > 0;
    }
}
