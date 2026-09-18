namespace CsPreprocessor;

public enum LineDirectiveState
{
    Remapped,
    Default,
    Hidden,
    RemappedSpan
}

// An active #line directive captured for later display remap (D5). Does not affect
// offsets. OriginalLine is the 1-based original source line the directive applies from.
// Full #line parsing arrives in T4.3; this is the shape only.
public sealed record LineDirective(int OriginalLine, int? MappedLine, string? FilePath, LineDirectiveState State);
