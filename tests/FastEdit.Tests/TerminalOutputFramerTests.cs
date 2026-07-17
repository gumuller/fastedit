using FastEdit.Infrastructure;
using Xunit;

namespace FastEdit.Tests;

public class TerminalOutputFramerTests
{
    private const string CommandToken = "00112233445566778899aabbccddeeff";

    [Fact]
    public void Append_OrdinaryLineSplitAcrossChunks_EmitsSafePartialContentPromptly()
    {
        var framer = new TerminalOutputFramer(emitPartialChunks: true);

        var firstFrames = framer.Append("ordinary par");
        var secondFrames = framer.Append("tial output\r\n");
        var completedFrames = framer.Complete();

        Assert.Equal("ordinary par", Assert.Single(firstFrames).Text);
        Assert.Equal("tial output\n", Assert.Single(secondFrames).Text);
        Assert.Empty(completedFrames);
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
    public void Append_StreamingStdout_RetainsSplitColorSequenceUntilComplete()
    {
        var framer = new TerminalOutputFramer(emitPartialChunks: true);

        Assert.Empty(framer.Append("\x1B[31"));
        var frames = framer.Append("mRED\x1B[0m\n");

        Assert.Equal("RED\n", Assert.Single(frames).Text);
    }

    [Fact]
    public void Append_StreamingStdout_EmitsTextBeforeSplitResetWithoutLeakingEscape()
    {
        var framer = new TerminalOutputFramer(emitPartialChunks: true);

        var firstFrames = framer.Append("\x1B[31mRED\x1B[0");
        var secondFrames = framer.Append("m plain");

        Assert.Equal("RED", Assert.Single(firstFrames).Text);
        Assert.Equal(" plain", Assert.Single(secondFrames).Text);
    }

    [Fact]
    public void Append_StreamingStdout_SplitResetBeforeSentinelPreservesFraming()
    {
        var framer = new TerminalOutputFramer(emitPartialChunks: true);
        var frames = new List<TerminalOutputFrame>();

        frames.AddRange(framer.Append("\x1B[31mRED\x1B[0"));
        frames.AddRange(framer.Append(
            $"m\n\n{TerminalOutputFramer.SentinelPrefix}42|{CommandToken}|C:\\work\n"));

        Assert.Equal(
            "RED\n",
            string.Concat(frames
                .Where(frame => frame.Kind == TerminalOutputFrameKind.Output)
                .Select(frame => frame.Text)));
        var sentinel = Assert.Single(
            frames,
            frame => frame.Kind == TerminalOutputFrameKind.Sentinel);
        Assert.True(sentinel.IsValidSentinel);
        Assert.Equal(42, sentinel.CommandId);
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
    public void Append_StreamingStdout_StripsAnsiSequenceSplitAcrossChunks()
    {
        var framer = new TerminalOutputFramer(emitPartialChunks: true);

        Assert.Empty(framer.Append("\x1B[31"));
        var frames = framer.Append(
            $"mRED\x1B[0m\n\n{TerminalOutputFramer.SentinelPrefix}42|{CommandToken}|C:\\work\n");

        Assert.Equal("RED\n", string.Concat(
            frames.Where(frame => frame.Kind == TerminalOutputFrameKind.Output)
                .Select(frame => frame.Text)));
        Assert.DoesNotContain('\x1B', string.Concat(frames.Select(frame => frame.Text)));
        Assert.Equal(TerminalOutputFrameKind.Sentinel, frames[^1].Kind);
    }

    [Fact]
    public void Append_StreamingStdout_StripsSplitAnsiResetWithoutDelayingText()
    {
        var framer = new TerminalOutputFramer(emitPartialChunks: true);
        var frames = new List<TerminalOutputFrame>();

        frames.AddRange(framer.Append("\x1B[31mRED\x1B[0"));
        frames.AddRange(framer.Append("m"));
        frames.AddRange(framer.Append(
            $"\n\n{TerminalOutputFramer.SentinelPrefix}42|{CommandToken}|C:\\work\n"));

        Assert.Equal("RED\n", string.Concat(
            frames.Where(frame => frame.Kind == TerminalOutputFrameKind.Output)
                .Select(frame => frame.Text)));
        Assert.Equal("RED", frames[0].Text);
        Assert.Equal(TerminalOutputFrameKind.Sentinel, frames[^1].Kind);
    }

    [Fact]
    public void Append_StreamingStdout_BoundsMalformedAnsiAndRecoversImmediately()
    {
        var framer = new TerminalOutputFramer(emitPartialChunks: true);
        var frames = new List<TerminalOutputFrame>();
        var largestBufferedLength = 0;

        for (var chunkIndex = 0; chunkIndex < 20; chunkIndex++)
        {
            var chunk = chunkIndex == 0
                ? "\x1B[" + new string('3', 32)
                : new string('3', 32);
            frames.AddRange(framer.Append(chunk));
            largestBufferedLength = Math.Max(
                largestBufferedLength,
                framer.BufferedCharacterCount);
        }

        var malformedOutput = string.Concat(frames.Select(frame => frame.Text));
        Assert.NotEmpty(malformedOutput);
        Assert.All(malformedOutput, character => Assert.Equal('3', character));
        Assert.InRange(
            largestBufferedLength,
            0,
            TerminalOutputFramer.MaxRetainedAnsiSequenceLength);

        frames.AddRange(framer.Append(
            $"RECOVERED\n\n{TerminalOutputFramer.SentinelPrefix}42|{CommandToken}|C:\\work\n"));

        Assert.EndsWith("RECOVERED\n", string.Concat(
            frames.Where(frame => frame.Kind == TerminalOutputFrameKind.Output)
                .Select(frame => frame.Text)));
        Assert.True(framer.BufferedCharacterCount <=
                    TerminalOutputFramer.MaxRetainedAnsiSequenceLength);
        Assert.Single(
            frames,
            frame => frame.Kind == TerminalOutputFrameKind.Sentinel &&
                     frame.IsValidSentinel);
    }

    [Fact]
    public void Append_StreamingStdout_NearLimitCsiStillCompletes()
    {
        var framer = new TerminalOutputFramer(emitPartialChunks: true);
        var parameters = new string(
            '3',
            TerminalOutputFramer.MaxRetainedAnsiSequenceLength - 2);

        Assert.Empty(framer.Append("\x1B[" + parameters));
        Assert.Equal(
            TerminalOutputFramer.MaxRetainedAnsiSequenceLength,
            framer.BufferedCharacterCount);

        var frame = Assert.Single(framer.Append("mVALID"));

        Assert.Equal("VALID", frame.Text);
        Assert.Equal(0, framer.BufferedCharacterCount);
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
        var framer = new TerminalOutputFramer(emitPartialChunks: true);

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
        var framer = new TerminalOutputFramer(emitPartialChunks: true);
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
