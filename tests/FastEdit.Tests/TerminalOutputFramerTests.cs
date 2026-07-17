using FastEdit.Infrastructure;
using Xunit;

namespace FastEdit.Tests;

public class TerminalOutputFramerTests
{
    private const string CommandToken = "00112233445566778899aabbccddeeff";

    [Fact]
    public void Append_OrdinaryLineSplitAcrossChunks_EmitsOneCompleteLine()
    {
        var framer = new TerminalOutputFramer();

        Assert.Empty(framer.Append("ordinary par"));
        var frames = framer.Append("tial output\r\n");

        var frame = Assert.Single(frames);
        Assert.Equal(TerminalOutputFrameKind.Output, frame.Kind);
        Assert.Equal("ordinary partial output\n", frame.Text);
    }

    [Fact]
    public void Append_StreamingStdout_EmitsSafePartialAndRetainsPossibleSentinel()
    {
        var framer = new TerminalOutputFramer(emitPartialChunks: true);

        Assert.Equal("O1", Assert.Single(framer.Append("O1")).Text);
        Assert.Empty(framer.Append("##FASTEDIT_SENT"));
        Assert.Empty(framer.Append($"INEL##42|{CommandToken}|C:\\work"));
        var sentinel = Assert.Single(framer.Append("\n"));

        Assert.Equal(TerminalOutputFrameKind.Sentinel, sentinel.Kind);
        Assert.True(sentinel.IsValidSentinel);
    }

    [Fact]
    public void Append_StreamingStdout_PreservesLaterLineTerminators()
    {
        var framer = new TerminalOutputFramer(emitPartialChunks: true);
        var frames = new List<TerminalOutputFrame>();

        frames.AddRange(framer.Append("first"));
        frames.AddRange(framer.Append("\n"));
        frames.AddRange(framer.Append("second"));
        frames.AddRange(framer.Append(
            $"\n\n{TerminalOutputFramer.SentinelPrefix}42|{CommandToken}|C:\\work\n"));

        Assert.Equal(
            "first\nsecond\n",
            string.Concat(frames
                .Where(frame => frame.Kind == TerminalOutputFrameKind.Output)
                .Select(frame => frame.Text)));
        Assert.Equal(TerminalOutputFrameKind.Sentinel, frames[^1].Kind);
    }

    [Fact]
    public void Append_StreamingStdout_PreservesBlankBeforeLaterPartialOutput()
    {
        var framer = new TerminalOutputFramer(emitPartialChunks: true);
        var frames = new List<TerminalOutputFrame>();

        frames.AddRange(framer.Append("\n"));
        frames.AddRange(framer.Append("partial"));
        frames.AddRange(framer.Append(
            $"\n\n{TerminalOutputFramer.SentinelPrefix}42|{CommandToken}|C:\\work\n"));

        Assert.Equal(
            "\npartial\n",
            string.Concat(frames
                .Where(frame => frame.Kind == TerminalOutputFrameKind.Output)
                .Select(frame => frame.Text)));
        Assert.Equal(TerminalOutputFrameKind.Sentinel, frames[^1].Kind);
    }

    [Fact]
    public void Append_ConsumesOnlyProtocolSeparatorBlankLine()
    {
        var framer = new TerminalOutputFramer(emitPartialChunks: true);

        var frames = framer.Append(
            $"HELLO\n\n{TerminalOutputFramer.SentinelPrefix}42|{CommandToken}|C:\\work\n");

        Assert.Equal(2, frames.Count);
        Assert.Equal("HELLO\n", frames[0].Text);
        Assert.Equal(TerminalOutputFrameKind.Sentinel, frames[1].Kind);
    }

    [Fact]
    public void Append_PreservesGenuineBlankLineBeforeProtocolSeparator()
    {
        var framer = new TerminalOutputFramer(emitPartialChunks: true);

        var frames = framer.Append(
            $"HELLO\n\n\n{TerminalOutputFramer.SentinelPrefix}42|{CommandToken}|C:\\work\n");

        Assert.Equal("HELLO\n\n", string.Concat(
            frames.Where(frame => frame.Kind == TerminalOutputFrameKind.Output)
                .Select(frame => frame.Text)));
        Assert.Equal(TerminalOutputFrameKind.Sentinel, frames[^1].Kind);
    }

    [Fact]
    public void Append_SentinelSplitAcrossChunks_ParsesOnceAndNeverEmitsMarker()
    {
        var framer = new TerminalOutputFramer();

        Assert.Empty(framer.Append("##FASTEDIT_SENT"));
        Assert.Empty(framer.Append($"INEL##42|{CommandToken}|C:\\work"));
        var frames = framer.Append("\r\n");

        var frame = Assert.Single(frames);
        Assert.Equal(TerminalOutputFrameKind.Sentinel, frame.Kind);
        Assert.True(frame.IsValidSentinel);
        Assert.Equal(42, frame.CommandId);
        Assert.Equal(Guid.ParseExact(CommandToken, "N"), frame.CommandToken);
        Assert.Equal(@"C:\work", frame.WorkingDirectory);
        Assert.Equal("", frame.Text);
    }

    [Fact]
    public void Append_MultipleChunkedLines_PreservesPerStreamOrder()
    {
        var framer = new TerminalOutputFramer();

        var firstFrames = framer.Append("first\nsec");
        var secondFrames = framer.Append("ond\nthird\n");

        Assert.Equal("first\n", Assert.Single(firstFrames).Text);
        Assert.Equal(
            ["second\n", "third\n"],
            secondFrames.Select(frame => frame.Text));
    }

    [Fact]
    public void Complete_PreservesOrdinaryUnterminatedOutput()
    {
        var framer = new TerminalOutputFramer();
        Assert.Empty(framer.Append("unterminated"));

        var frame = Assert.Single(framer.Complete());

        Assert.Equal(TerminalOutputFrameKind.Output, frame.Kind);
        Assert.Equal("unterminated", frame.Text);
        Assert.Empty(framer.Complete());
    }

    [Fact]
    public void Append_LongUnterminatedOutput_BoundsBufferWithoutLosingText()
    {
        var framer = new TerminalOutputFramer();
        var input = new string('x', 20000);

        var frames = framer.Append(input);
        var completedFrames = framer.Complete();

        Assert.Equal(
            input,
            string.Concat(frames.Concat(completedFrames).Select(frame => frame.Text)));
    }

    [Fact]
    public void Append_StderrModePreservesSplitMarkerLikeTextAsOrdinaryOutput()
    {
        var framer = new TerminalOutputFramer(parseSentinels: false);

        Assert.Empty(framer.Append("stderr ##FASTEDIT_SENT"));
        var frame = Assert.Single(framer.Append(
            $"INEL##42|{CommandToken}|not-a-control-frame\n"));

        Assert.Equal(TerminalOutputFrameKind.Output, frame.Kind);
        Assert.Equal(
            $"stderr ##FASTEDIT_SENTINEL##42|{CommandToken}|not-a-control-frame\n",
            frame.Text);
    }

    [Theory]
    [InlineData("##FASTEDIT_SENTINEL##not-an-id|C:\\work\n")]
    [InlineData("##FASTEDIT_SENTINEL##42-no-pipe\n")]
    [InlineData("##FASTEDIT_SENTINEL##0|C:\\work\n")]
    [InlineData("##FASTEDIT_SENTINEL##42|not-a-token|C:\\work\n")]
    public void Append_InvalidSentinel_IsHiddenAndRejected(string text)
    {
        var frame = Assert.Single(new TerminalOutputFramer().Append(text));

        Assert.Equal(TerminalOutputFrameKind.Sentinel, frame.Kind);
        Assert.False(frame.IsValidSentinel);
        Assert.Equal("", frame.Text);
    }
}
