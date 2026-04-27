using System.Diagnostics;
using System.Text;

namespace FastEdit.Tests;

public sealed class TraceCapture : TraceListener
{
    private readonly StringBuilder _messages = new();

    public TraceCapture()
    {
        Trace.Listeners.Add(this);
    }

    public string Messages
    {
        get
        {
            lock (_messages)
            {
                return _messages.ToString();
            }
        }
    }

    public override void Write(string? message)
    {
        lock (_messages)
        {
            _messages.Append(message);
        }
    }

    public override void WriteLine(string? message)
    {
        lock (_messages)
        {
            _messages.AppendLine(message);
        }
    }

    public override void TraceEvent(
        TraceEventCache? eventCache,
        string source,
        TraceEventType eventType,
        int id,
        string? message)
    {
        WriteLine(message);
    }

    public override void TraceEvent(
        TraceEventCache? eventCache,
        string source,
        TraceEventType eventType,
        int id,
        string? format,
        params object?[]? args)
    {
        WriteLine(args is { Length: > 0 } ? string.Format(format ?? "", args) : format);
    }

    protected override void Dispose(bool disposing)
    {
        Trace.Listeners.Remove(this);
        base.Dispose(disposing);
    }
}
