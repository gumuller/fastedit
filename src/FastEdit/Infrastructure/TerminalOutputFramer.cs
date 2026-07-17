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
    private readonly AnsiEscapeFilter _ansiFilter = new();
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

        for (var index = _scanIndex; index < _pending.Length; index++)
        {
            if (_pending[index] != '\n')
                continue;

            AddCompleteLineFrames(_pending.ToString(start, index - start), frames);
            start = index + 1;
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
        _ansiFilter.Complete();
        return frames;
    }

    private void AddCompleteLineFrames(string rawLine, List<TerminalOutputFrame> frames)
    {
        var line = rawLine.TrimEnd('\r');
        if (line.Length == 0 && _hasEmittedPartialLineContent)
        {
            _ansiFilter.EndLine();
            AddDeferredBlankLines(frames, 1);
            _hasEmittedPartialLineContent = false;
            return;
        }

        if (_parseSentinels && line.Length == 0)
        {
            _ansiFilter.EndLine();
            _deferredBlankLineCount++;
            return;
        }

        var frame = CreateFrame(rawLine, hasNewLine: true);
        _ansiFilter.EndLine();
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

        var cleaned = _ansiFilter.Filter(line);
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
        length -= GetIncompleteAnsiSuffixLength(_pending.ToString(0, length));
        if (length <= 0)
            return;

        var output = _ansiFilter.Filter(_pending.ToString(0, length));
        _pending.Remove(0, length);
        _scanIndex = _pending.Length;
        if (string.IsNullOrEmpty(output))
            return;

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

    private static int GetIncompleteAnsiSuffixLength(string text)
    {
        var escapeIndex = text.LastIndexOf('\x1B');
        if (escapeIndex < 0)
            return 0;

        if (escapeIndex == text.Length - 1)
            return 1;

        if (text[escapeIndex + 1] != '[')
            return 0;

        for (var index = escapeIndex + 2; index < text.Length; index++)
        {
            if (text[index] is >= '\x40' and <= '\x7E')
                return 0;
        }

        return text.Length - escapeIndex;
    }

    private static TerminalOutputFrame ParseSentinel(string payload)
    {
        var idSeparator = payload.IndexOf('|');
        var tokenSeparator = idSeparator < 0 ? -1 : payload.IndexOf('|', idSeparator + 1);
        if (idSeparator <= 0 ||
            tokenSeparator <= idSeparator + 1 ||
            !int.TryParse(
                payload.AsSpan(0, idSeparator),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var commandId) ||
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

    private sealed class AnsiEscapeFilter
    {
        private const int MaxPendingEscapeLength = 256;
        private readonly StringBuilder _pendingEscape = new();
        private bool _discardingCsi;

        public string Filter(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            var output = new StringBuilder(text.Length);
            foreach (var character in text)
            {
                if (_discardingCsi)
                {
                    if (character is >= '\x40' and <= '\x7E')
                    {
                        _discardingCsi = false;
                    }
                    else if (character is < '\x20' or > '\x3F')
                    {
                        _discardingCsi = false;
                        if (character == '\x1B')
                            _pendingEscape.Append(character);
                        else
                            output.Append(character);
                    }
                    continue;
                }

                if (_pendingEscape.Length == 0)
                {
                    if (character == '\x1B')
                        _pendingEscape.Append(character);
                    else
                        output.Append(character);
                    continue;
                }

                if (_pendingEscape.Length == 1)
                {
                    if (character == '[')
                    {
                        _pendingEscape.Append(character);
                        continue;
                    }

                    output.Append(_pendingEscape);
                    _pendingEscape.Clear();
                    if (character == '\x1B')
                        _pendingEscape.Append(character);
                    else
                        output.Append(character);
                    continue;
                }

                if (character is >= '\x40' and <= '\x7E')
                {
                    _pendingEscape.Clear();
                    continue;
                }

                if (character is >= '\x20' and <= '\x3F')
                {
                    if (_pendingEscape.Length >= MaxPendingEscapeLength)
                    {
                        _pendingEscape.Clear();
                        _discardingCsi = true;
                    }
                    else
                    {
                        _pendingEscape.Append(character);
                    }
                    continue;
                }

                _pendingEscape.Clear();
                if (character == '\x1B')
                    _pendingEscape.Append(character);
                else
                    output.Append(character);
            }

            return output.ToString();
        }

        public void EndLine()
        {
            _pendingEscape.Clear();
            _discardingCsi = false;
        }

        public void Complete()
        {
            _pendingEscape.Clear();
            _discardingCsi = false;
        }
    }
}
