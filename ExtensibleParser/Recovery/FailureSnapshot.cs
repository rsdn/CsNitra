namespace ExtensibleParser.Recovery;

public sealed record FailureSnapshot(
    int Pos,
    StackFrame[] Stack,
    Terminal FailedTerminal,
    Terminal[] Expected
);
