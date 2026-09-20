namespace ExtensibleParser.Recovery;

// D2-full (6.2.1): observation-only metrics for the last recovery session (one Parse).
//
// Counters are incremented in the recovery loop (accept/rollback per strategy, keyed by the
// candidate's Rank == strategy index S0..S6) and the specCache hits/misses read through the
// SpeculativeCache (single source of truth — those counters are already incremented at the
// specCache lookup site, SpeculativeCache.Speculative, B2/5a.1).
//
// Time-by-phase and hygiene before/after counters are 6.2.2 (the hygiene fields are reserved below).
// Everything here is observation-only: no recovery behavior depends on it.
public sealed class RecoveryMetrics(SpeculativeCache specCache)
{
    private readonly SpeculativeCache _specCache = specCache;

    // accept/rollback per strategy, indexed by rank 0..6 (S0..S6). A candidate's Rank IS its
    // strategy rank (RecoveryCandidate: "рангом стратегии (S0..S5)"); S1b is part of S1 (rank 1).
    private readonly int[] _accept = new int[7];
    private readonly int[] _rollback = new int[7];

    // specCache hits/misses — read through the SpeculativeCache (single source of truth; the
    // counters are incremented at the lookup site, so they are always consistent with the cache).
    public int SpecCacheHits => _specCache.Hits;

    public int SpecCacheMisses => _specCache.Misses;

    // 6.2.2 reserved (fields now, wired in 6.2.2): hygiene removals before/after B1.
    public int HygieneRemovalsBefore { get; internal set; }

    public int HygieneRemovalsAfter { get; internal set; }

    // rank 0..6 == strategy index (S0..S6).
    public void NoteAccept(int rank) => _accept[rank]++;

    public void NoteRollback(int rank) => _rollback[rank]++;

    public int AcceptCount(RecoveryStrategy strategy) => _accept[Index(strategy)];

    public int AcceptCount(int rank) => _accept[rank];

    public int RollbackCount(RecoveryStrategy strategy) => _rollback[Index(strategy)];

    public int RollbackCount(int rank) => _rollback[rank];

    public int TotalAccept => _accept.Sum();

    public int TotalRollback => _rollback.Sum();

    // Per-session reset (called at the start of each Recover). The specCache hits/misses are reset by
    // the cache's own Reset (read through here); this resets the per-strategy arrays + reserved fields.
    public void Reset()
    {
        for (var i = 0; i < _accept.Length; i++)
        {
            _accept[i] = 0;
            _rollback[i] = 0;
        }
        HygieneRemovalsBefore = 0;
        HygieneRemovalsAfter = 0;
    }

    private static int Index(RecoveryStrategy strategy) =>
        strategy switch
        {
            RecoveryStrategy.S0 => 0,
            RecoveryStrategy.S1 => 1,
            RecoveryStrategy.S2 => 2,
            RecoveryStrategy.S3 => 3,
            RecoveryStrategy.S4 => 4,
            RecoveryStrategy.S5 => 5,
            RecoveryStrategy.S6 => 6,
            _ => throw new ArgumentException("Expected a single strategy", nameof(strategy)),
        };
}
