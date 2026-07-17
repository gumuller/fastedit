using System.Globalization;
using System.Text;
using FastEdit.Services;

namespace FastEdit.Infrastructure;

internal enum TerminalOutputFrameKind
{
    Output,
    Sentinel
}

internal readonly record struct TerminalOutputFrame(
    TerminalOutputFrameKind Kind,
    string Text,
    int CommandId,
    Guid CommandToken,
    string WorkingDirectory,
    bool IsValidSentinel);

internal sealed class TerminalOutputFramer
{
    internal const string SentinelPrefix = "##FASTEDIT_SENTINEL##";
    private const int MaxBufferedOutputLength = 8192;
    private const int MaxSentinelLength = 65536;

    private readonly StringBuilder _pending = new();
    private readonly bool _parseSentinels;
    private readonly bool _emitPartialChunks;
    private int _scanIndex;
    private int _deferredBlankLineCount;
    private bool _hasEmittedPartialLineContent;

    public TerminalOutputFramer(bool parseSentinels = true, bool emitPartialChunks = false)
    {
        _parseSentinels = parseSentinels;
        _emitPartialChunks = emitPartialChunks;
    }

    public IReadOnlyList<TerminalOutputFrame> Append(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<TerminalOutputFrame>();

        _pending.Append(text);
        var frames = new List<TerminalOutputFrame>();
        var start = 0;

        for (var i = _scanIndex; i < _pending.Length; i++)
        {
            if (_pending[i] != '\n')
                continue;

            AddCompleteLineFrames(_pending.ToString(start, i - start), frames);
            start = i + 1;
        }

        if (start > 0)
        {
            _pending.Remove(0, start);
            _scanIndex = 0;
        }
        else
        {
            _scanIndex = _pending.Length;
        }

        DrainSafePartialOutput(frames);

        return frames;
    }

    public IReadOnlyList<TerminalOutputFrame> Complete()
    {
        var frames = new List<TerminalOutputFrame>();
        AddDeferredBlankLines(frames, _deferredBlankLineCount);
        _deferredBlankLineCount = 0;
        if (_pending.Length > 0)
            frames.Add(CreateFrame(_pending.ToString(), hasNewLine: false));
        _pending.Clear();
        _scanIndex = 0;
        _hasEmittedPartialLineContent = false;
        return frames;
    }

    private void AddCompleteLineFrames(string rawLine, List<TerminalOutputFrame> frames)
    {
        var line = rawLine.TrimEnd('\r');
        if (line.Length == 0 && _hasEmittedPartialLineContent)
        {
            AddDeferredBlankLines(frames, 1);
            _hasEmittedPartialLineContent = false;
            return;
        }

        if (_parseSentinels && line.Length == 0)
        {
            _deferredBlankLineCount++;
            return;
        }

        var frame = CreateFrame(rawLine, hasNewLine: true);
        var blankLinesToEmit = frame.Kind == TerminalOutputFrameKind.Sentinel
            ? Math.Max(0, _deferredBlankLineCount - 1)
            : _deferredBlankLineCount;
        AddDeferredBlankLines(frames, blankLinesToEmit);
        _deferredBlankLineCount = 0;
        frames.Add(frame);
        _hasEmittedPartialLineContent = false;
    }

    private static void AddDeferredBlankLines(
        List<TerminalOutputFrame> frames,
        int blankLineCount)
    {
        for (var index = 0; index < blankLineCount; index++)
        {
            frames.Add(new TerminalOutputFrame(
                TerminalOutputFrameKind.Output,
                "\n",
                0,
                Guid.Empty,
                "",
                false));
        }
    }

    private TerminalOutputFrame CreateFrame(string rawLine, bool hasNewLine)
    {
        var line = rawLine.TrimEnd('\r');
        var sentinelIndex = _parseSentinels
            ? line.IndexOf(SentinelPrefix, StringComparison.Ordinal)
            : -1;
        if (sentinelIndex >= 0)
            return ParseSentinel(line[(sentinelIndex + SentinelPrefix.Length)..]);

        var cleaned = CommandRunnerService.StripAnsiCodes(line);

        return new TerminalOutputFrame(
            TerminalOutputFrameKind.Output,
            hasNewLine ? cleaned + "\n" : cleaned,
            0,
            Guid.Empty,
            "",
            false);
    }

    private void DrainSafePartialOutput(List<TerminalOutputFrame> frames)
    {
        if (_parseSentinels)
        {
            var pendingText = _pending.ToString();
            var sentinelIndex = pendingText.IndexOf(SentinelPrefix, StringComparison.Ordinal);
            if (sentinelIndex >= 0)
            {
                if (sentinelIndex > 0)
                    EmitPartialPrefix(frames, sentinelIndex);

                if (_pending.Length > MaxSentinelLength)
                {
                    _pending.Clear();
                    _scanIndex = 0;
                }
                return;
            }

            var possibleSentinelPrefixLength = GetPossibleSentinelPrefixLength(pendingText);
            if (_emitPartialChunks || _pending.Length > MaxBufferedOutputLength)
                EmitPartialPrefix(frames, _pending.Length - possibleSentinelPrefixLength);
            return;
        }

        if (_emitPartialChunks || _pending.Length > MaxBufferedOutputLength)
            EmitPartialPrefix(frames, _pending.Length);
    }

    private void EmitPartialPrefix(List<TerminalOutputFrame> frames, int length)
    {
        if (length <= 0)
            return;

        var output = CommandRunnerService.StripAnsiCodes(_pending.ToString(0, length));
        _pending.Remove(0, length);
        _scanIndex = _pending.Length;
        if (!string.IsNullOrEmpty(output))
        {
            AddDeferredBlankLines(frames, _deferredBlankLineCount);
            _deferredBlankLineCount = 0;
            _hasEmittedPartialLineContent = true;
            frames.Add(new TerminalOutputFrame(
                TerminalOutputFrameKind.Output,
                output,
                0,
                Guid.Empty,
                "",
                false));
        }
    }

    private static int GetPossibleSentinelPrefixLength(string text)
    {
        var maxLength = Math.Min(text.Length, SentinelPrefix.Length - 1);
        for (var length = maxLength; length > 0; length--)
        {
            if (text.AsSpan(text.Length - length).SequenceEqual(SentinelPrefix.AsSpan(0, length)))
                return length;
        }

        return 0;
    }

    private static TerminalOutputFrame ParseSentinel(string payload)
    {
        var idSeparator = payload.IndexOf('|');
        var tokenSeparator = idSeparator < 0 ? -1 : payload.IndexOf('|', idSeparator + 1);
        if (idSeparator <= 0 ||
            tokenSeparator <= idSeparator + 1 ||
            !int.TryParse(payload.AsSpan(0, idSeparator), NumberStyles.None, CultureInfo.InvariantCulture, out var commandId) ||
            !Guid.TryParseExact(
                payload.AsSpan(idSeparator + 1, tokenSeparator - idSeparator - 1),
                "N",
                out var commandToken) ||
            commandId <= 0)
        {
            return new TerminalOutputFrame(
                TerminalOutputFrameKind.Sentinel,
                "",
                0,
                Guid.Empty,
                "",
                false);
        }

        var workingDirectory = payload[(tokenSeparator + 1)..].Trim();
        return new TerminalOutputFrame(
            TerminalOutputFrameKind.Sentinel,
            "",
            commandId,
            commandToken,
            workingDirectory,
            true);
    }
}
