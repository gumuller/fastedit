namespace FastEdit.Infrastructure;

internal static class EditorStateTransitionCoordinator
{
    public static void Transition<T>(
        T? outgoing,
        T? incoming,
        Action<T> captureOutgoing,
        Action<T> detachOutgoing,
        Action<T> initializeIncoming,
        Action<T> scheduleIncomingRestore)
        where T : class
    {
        if (outgoing != null)
        {
            captureOutgoing(outgoing);
            detachOutgoing(outgoing);
        }

        if (incoming == null)
            return;

        initializeIncoming(incoming);
        scheduleIncomingRestore(incoming);
    }

    public static int ClampCursorOffset(int offset, int documentLength) =>
        Math.Clamp(offset, 0, Math.Max(0, documentLength));

    public static double ClampScrollOffset(double offset) =>
        double.IsFinite(offset) ? Math.Max(0, offset) : 0;
}
