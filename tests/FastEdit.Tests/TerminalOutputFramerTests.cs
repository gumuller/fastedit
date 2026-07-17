using FastEdit.Infrastructure;
using Xunit;

namespace FastEdit.Tests;

public class TerminalOutputFramerTests
{
    private const string CommandToken = "00112233445566778899aabbccddeeff";

    [Fact]
    public void Append_OrdinaryLineSplitAcrossChunks_EmitsSafePartialContentPromptly()
    {
        var framer = new TerminalOutputFramer();

        var firstFrames = framer.Append("ordinary par");
        var secondFrames = framer.Append("tial output\r\n");
        var completedFrames = framer.Complete();

        Assert.Equal("ordinary par", Assert.Single(firstFrames).Text);
        Assert.Equal("tial output", Assert.Single(secondFrames).Text);
        Assert.Equal("\n", Assert.Single(completedFrames).Text);
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
    public void Append_ProtocolSeparatorBeforeSentinel_IsConsumedWithoutDroppingUserBlankLines()
    {
        var framer = new TerminalOutputFramer();

        var frames = framer.Append(
            $"HELLO\n\n\n{TerminalOutputFramer.SentinelPrefix}42|{CommandToken}|C:\\work\r\n");

        Assert.Equal("HELLO\n\n", string.Concat(
            frames.Where(frame => frame.Kind == TerminalOutputFrameKind.Output)
                .Select(frame => frame.Text)));
        Assert.Single(frames, frame => frame.Kind == TerminalOutputFrameKind.Sentinel);
    }

    [Fact]
    public void Append_MultipleChunkedLines_PreservesPerStreamOrder()
    {
        var framer = new TerminalOutputFramer();

        var firstFrames = framer.Append("first\nsec");
        var secondFrames = framer.Append("ond\nthird\n");
        var completedFrames = framer.Complete();

        Assert.Equal(
            "first\nsecond\nthird\n",
            string.Concat(firstFrames.Concat(secondFrames).Concat(completedFrames)
                .Select(frame => frame.Text)));
    }

    [Fact]
    public void Complete_PreservesOrdinaryUnterminatedOutput()
    {
        var framer = new TerminalOutputFramer();
        var frames = framer.Append("unterminated");

        var frame = Assert.Single(frames);

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
