namespace ExtensibleParser.Recovery;

public sealed record ParseContext(
    string RuleName,
    FrameLocation Location,
    Terminal[] Expected,
    RecoveryOptions? Options
);
