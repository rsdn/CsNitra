namespace CsPreprocessor;

public enum DiagnosticSeverity
{
    Error,
    Warning
}

// A preprocessor diagnostic. StartPos/EndPos are in ORIGINAL source coordinates:
// 0-based, EndPos exclusive. Code is an optional stable diagnostic id.
public sealed record Diagnostic(string Message, int StartPos, int EndPos, DiagnosticSeverity Severity, string? Code = null);
