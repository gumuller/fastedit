namespace FastEdit.Infrastructure;

public static class TabReorderPlanner
{
    public static bool TryCreateMove(int sourceIndex, int targetIndex, out TabReorderMove move)
    {
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            move = default;
            return false;
        }

        move = new TabReorderMove(sourceIndex, targetIndex);
        return true;
    }
}

public readonly record struct TabReorderMove(int SourceIndex, int TargetIndex);
