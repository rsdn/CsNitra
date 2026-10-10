namespace ExtensibleParser.Recovery;

public readonly record struct StackFrame(
    string RuleName,
    int Precedence,
    FrameLocation Location,
    Terminal[]? Expected,
    RecoveryOptions? Options
);

public abstract record FrameLocation;

public sealed record RuleFrameLocation(int AltIndex) : FrameLocation;

public sealed record SeqFrameLocation(int ElementIndex) : FrameLocation
{
    // The exact grammar occurrence, including nested sequences and transformed TDOPP suffixes.
    // Synthetic snapshots may omit it and use the legacy alternative lookup.
    public Seq? Sequence { get; init; }
}

public sealed record LoopFrameLocation(string LoopKind, int Iteration) : FrameLocation
{
    public Rule? Element { get; init; }
}

public sealed record PostfixFrameLocation(int PostfixIndex) : FrameLocation;
