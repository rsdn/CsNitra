using System.Threading;

namespace ExtensibleParser.Recovery;

public enum RecoveryMode { Ide, Compiler, Test }

// A4-5/B4: маска стратегий S0..S6 (сейчас все включены). StrategyMask пока не читается.
[Flags]
public enum RecoveryStrategy
{
    None = 0,
    // S0 (base re-parse) is always tried first; it is not gated by the StrategyMask (the
    // mask is not read yet) - the bit is present for mask completeness, not as a runtime gate.
    S0 = 1 << 0,
    S1 = 1 << 1,
    S2 = 1 << 2,
    S3 = 1 << 3,
    S4 = 1 << 4,
    S5 = 1 << 5,
    S6 = 1 << 6,
    All = S0 | S1 | S2 | S3 | S4 | S5 | S6
}

// A3: единый источник recovery-лимитов. Значения по умолчанию = предыдущим хардкодам.
public sealed record RecoveryProfile
{
    public RecoveryMode Mode { get; init; } = RecoveryMode.Ide;

    // Лимит recovery-цикла (был Parser.MaxRecoveryIterations = 1000).
    public int MaxIterations { get; init; } = 1000;

    // Per-tier sub-budgets (A2; были Parser.S1/S2/S3S6TierBudget).
    public int S1TierBudget { get; init; } = 4;
    public int S2TierBudget { get; init; } = 2;
    public int S3S6TierBudget { get; init; } = 2;

    // MaxSkip для S2/S3 (был RecoveryEngine.DefaultMaxSkip = 1000).
    public int MaxSkip { get; init; } = 1000;

    // Depth guard (был input.Length * 4 + 128 в SetMaxParseDepth).
    public int MaxParseDepthBase { get; init; } = 128;
    public int MaxParseDepthPerChar { get; init; } = 4;

    // 7.1.1/R3: absolute cap on parse depth — a STACK-SAFETY constant, not a per-profile limit.
    // The per-char limit above is unbounded, but a thread's stack budget is a FIXED number of
    // ParseAlternative frames regardless of input length: measured on a 1.5MB .NET 8 thread the
    // real stack overflows at ~575-615 frames (both the worst-case self-recursive grammar
    // `Expr := "(" Expr ")" | Digits` — 295 nesting levels OK, 300 crash — and the C# grammar —
    // ~80 nested types OK, ~85 crash). The cap (400) keeps ~30% margin below that threshold and
    // stays above legitimate parse depths (CSharpGrammarTests max ~150 frames; 7.2 frames per
    // nested-type level → ~55 levels of nesting still parse cleanly).
    public const int MaxParseDepthCap = 400;

    // B4: wall-clock budget for the recovery session. When exceeded, DegradationLevel
    // is raised (see Parser). Test profile disables it (Infinite) for determinism.
    public TimeSpan TimeBudget { get; init; } = TimeSpan.FromMilliseconds(500);

    // TODO(A4-5/B4): маска стратегий — поле пока не читается.
    public RecoveryStrategy StrategyMask { get; init; } = RecoveryStrategy.All;

    // Профили по умолчанию.
    public static RecoveryProfile Ide { get; } = new() { Mode = RecoveryMode.Ide };
    public static RecoveryProfile Compiler { get; } = new() { Mode = RecoveryMode.Compiler, MaxIterations = 64 };
    // TimeBudget = Infinite: the time-check is disabled (see Parser guard `TimeBudget > TimeSpan.Zero`).
    public static RecoveryProfile Test { get; } = new() { Mode = RecoveryMode.Test, TimeBudget = Timeout.InfiniteTimeSpan };
}

// B4 degradation ladder: level -> effective strategy parameters.
// Level 0 = full (default Ide). Higher levels = coarser recovery.
// MaxSkipMultiplier: Level 1 x4 is an EXPANSION (wider cheap scan compensates
// for disabling the expensive S2 speculative parse), not a narrowing.
public sealed record DegradationLevel(int Index, RecoveryStrategy Mask, double MaxSkipMultiplier, bool Speculation, bool ForceS6);

public static class Degradation
{
    public static readonly DegradationLevel[] Levels =
    {
        new(0, RecoveryStrategy.All,            1.0, true,  false), // full
        new(1, RecoveryStrategy.All,            4.0, false, false), // S2 no-spec, MaxSkip x4 (expansion)
        new(2, RecoveryStrategy.S1 | RecoveryStrategy.S3 | RecoveryStrategy.S6, 0.5, false, false), // S1 + short S3 + S6
        new(3, RecoveryStrategy.S6,             1.0, false, true),  // hard limit: force S6 + stop
    };

    public static DegradationLevel Get(int index) => Levels[Math.Min(Math.Max(index, 0), Levels.Length - 1)];
}
