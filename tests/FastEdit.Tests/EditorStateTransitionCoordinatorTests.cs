using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class EditorStateTransitionCoordinatorTests
{
    [Fact]
    public void Transition_CapturesBeforeDetachAndRestoresAfterInitialization()
    {
        var first = new State("first", cursorOffset: 2);
        var second = new State("second", cursorOffset: 99);
        var editorOffset = 7;
        var operations = new List<string>();

        EditorStateTransitionCoordinator.Transition(
            first,
            second,
            outgoing =>
            {
                operations.Add($"capture:{outgoing.Name}");
                outgoing.CursorOffset = editorOffset;
            },
            outgoing => operations.Add($"detach:{outgoing.Name}"),
            incoming => operations.Add($"initialize:{incoming.Name}"),
            incoming =>
            {
                operations.Add($"restore:{incoming.Name}");
                editorOffset =
                    EditorStateTransitionCoordinator.ClampCursorOffset(
                        incoming.CursorOffset,
                        documentLength: 10);
            });

        Assert.Equal(
            new[]
            {
                "capture:first",
                "detach:first",
                "initialize:second",
                "restore:second"
            },
            operations);
        Assert.Equal(7, first.CursorOffset);
        Assert.Equal(10, editorOffset);

        operations.Clear();
        EditorStateTransitionCoordinator.Transition(
            second,
            first,
            outgoing =>
            {
                operations.Add($"capture:{outgoing.Name}");
                outgoing.CursorOffset = editorOffset;
            },
            outgoing => operations.Add($"detach:{outgoing.Name}"),
            incoming => operations.Add($"initialize:{incoming.Name}"),
            incoming =>
            {
                operations.Add($"restore:{incoming.Name}");
                editorOffset =
                    EditorStateTransitionCoordinator.ClampCursorOffset(
                        incoming.CursorOffset,
                        documentLength: 10);
            });

        Assert.Equal(10, second.CursorOffset);
        Assert.Equal(7, editorOffset);
        Assert.Equal("restore:first", operations[^1]);
    }

    [Theory]
    [InlineData(-4, 10, 0)]
    [InlineData(20, 10, 10)]
    [InlineData(4, 10, 4)]
    public void ClampCursorOffset_ConstrainsToDocument(
        int offset,
        int documentLength,
        int expected)
    {
        Assert.Equal(
            expected,
            EditorStateTransitionCoordinator.ClampCursorOffset(
                offset,
                documentLength));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(4.5, 4.5)]
    public void ClampScrollOffset_RejectsInvalidValues(
        double offset,
        double expected)
    {
        Assert.Equal(
            expected,
            EditorStateTransitionCoordinator.ClampScrollOffset(offset));
    }

    private sealed class State(string name, int cursorOffset)
    {
        public string Name { get; } = name;
        public int CursorOffset { get; set; } = cursorOffset;
    }
}
