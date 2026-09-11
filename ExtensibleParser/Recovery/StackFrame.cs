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

public sealed record SeqFrameLocation(int ElementIndex) : FrameLocation;

public sealed record LoopFrameLocation(string LoopKind, int Iteration) : FrameLocation;

public sealed record PostfixFrameLocation(int PostfixIndex) : FrameLocation;
