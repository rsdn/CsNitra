using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Diagnostics;

public enum LogImportance
{
    Non,
    Normal,
    High,
}

public sealed class Log
{
    public Log(LogImportance importance = LogImportance.Non)
    {
        Importance = importance;
    }

    public LogImportance Importance { get; set; } = LogImportance.Normal;

    public void Info(string text, LogImportance importance = LogImportance.Normal, [CallerMemberName] string? memberName = null, [CallerLineNumber] int line = 0)
    {
        if (importance >= Importance)
            Trace.TraceInformation($"{memberName} {line}: {text}");
    }
}
