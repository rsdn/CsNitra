namespace ExtensibleParser.Recovery;

public enum RecoveryKind { Inserted, Skipped, Unrecovered, Extraneous, InsufficientStack }

public sealed record RecoveryDiagnostic(
    int StartPos,
    int EndPos,
    RecoveryKind Kind,
    string Message,
    Terminal? Terminal,
    string? RuleName
);
