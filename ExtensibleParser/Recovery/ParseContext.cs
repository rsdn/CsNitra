namespace ExtensibleParser.Recovery;

public sealed record ParseContext(
    string RuleName,
    ParseLocation Location,
    Terminal[] Expected,
    RecoveryOptions? Options
);

public abstract record ParseLocation
{
    public virtual int GetDepth() => 0;
}

public sealed record SeqLocation(int ElementIndex) : ParseLocation;

public sealed record LoopLocation(string LoopKind, int Iteration) : ParseLocation;

public sealed record PrefixLocation(int PrefixIndex) : ParseLocation;

public sealed record RecoveryOptions
{
    public Terminal[]? Terminators { get; init; }
}
