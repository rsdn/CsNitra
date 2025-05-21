using System.Diagnostics;
using System.Runtime.CompilerServices;

#pragma warning disable IDE0130 // Namespace does not match folder structure

namespace Diagnostics;

public enum LogImportance
{
    Non,
    Normal,
    High,
}

public sealed class Log(LogImportance importance = LogImportance.Non)
{
    public LogImportance Importance { get; set; } = importance;

    public void Info(string text, LogImportance importance = LogImportance.Normal, [CallerMemberName] string? memberName = null, [CallerLineNumber] int line = 0)
    {
        if (importance >= Importance)
            Trace.TraceInformation($"{memberName} {line}: {text}");
    }
}
