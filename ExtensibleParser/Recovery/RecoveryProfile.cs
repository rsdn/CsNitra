namespace ExtensibleParser.Recovery;

public enum RecoveryMode { Ide, Compiler, Test }

// A4-5/B4: маска стратегий S0..S6 (сейчас все включены). StrategyMask пока не читается.
[Flags]
public enum RecoveryStrategy
{
    None = 0,
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

    // TODO(A4-5/B4): маска стратегий — поле пока не читается.
    public RecoveryStrategy StrategyMask { get; init; } = RecoveryStrategy.All;

    // Профили по умолчанию.
    public static RecoveryProfile Ide { get; } = new() { Mode = RecoveryMode.Ide };
    public static RecoveryProfile Compiler { get; } = new() { Mode = RecoveryMode.Compiler, MaxIterations = 64 };
    public static RecoveryProfile Test { get; } = new() { Mode = RecoveryMode.Test };
}
