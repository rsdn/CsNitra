using System.Diagnostics;

namespace Regex;

public sealed class Log
{
    public bool IsLoggingEnabled { get; set; } = true;

    public void Info(string text)
    {
        if (IsLoggingEnabled)
            Trace.TraceInformation(text);
    }
}
