namespace CsNitra.TypeChecking;

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info
}

public sealed partial record Diagnostic(
    string Message,
    SourceSpan Location,
    DiagnosticSeverity Severity
);
