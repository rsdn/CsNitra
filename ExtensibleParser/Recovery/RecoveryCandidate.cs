namespace ExtensibleParser.Recovery;

/// <summary>
/// Кандидат восстановления: набор патчей (инъекции/memo) с детерминированным Id,
/// рангом стратегии (S0..S5), точкой применения и стоимостью.
/// </summary>
public sealed record RecoveryCandidate(
    string Id,
    int Rank,
    int Pos,
    int Cost,
    string RuleName,
    string? TerminalKind,
    Action<Parser> Apply,
    Action<Parser> Rollback,
    RecoveryDiagnostic[] Diagnostics
);
