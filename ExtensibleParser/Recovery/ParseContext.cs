namespace ExtensibleParser.Recovery;

public sealed record ParseContext(
    string RuleName,
    FrameLocation Location,
    Terminal[] Expected,
    RecoveryOptions? Options
);

public sealed record RecoveryOptions
{
    public Terminal[]? Terminators { get; init; }
    public Rule[]? Anchors { get; init; }
    public Rule[]? CanStart { get; init; }
    public Terminal[]? TryInsert { get; init; }
    public int? MaxSkip { get; init; }
    public bool Recoverable { get; init; } = true;
}
