namespace FastEdit.Infrastructure;

public static class TerminalTabClosePolicy
{
    public static bool TryGetNextActiveIndex(
        int tabCountBeforeClose,
        int closedIndex,
        bool closedTabWasActive,
        out int nextActiveIndex)
    {
        var tabCountAfterClose = tabCountBeforeClose - 1;
        if (!closedTabWasActive || tabCountAfterClose <= 0 || closedIndex < 0)
        {
            nextActiveIndex = -1;
            return false;
        }

        nextActiveIndex = Math.Min(closedIndex, tabCountAfterClose - 1);
        return true;
    }
}
