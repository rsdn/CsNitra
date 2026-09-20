namespace ExtensibleParser.Recovery;

// A4-6 (6.3.1): observation-only grammar-quality diagnostics channel — feedback for the GRAMMAR
// AUTHOR about their recovery annotations. It is a SEPARATE public type from RecoveryMetrics (the
// per-recovery-session metrics: accept/rollback per strategy, specCache, phase times) and from
// RecoveryDiagnostics (the per-input error diagnostics). Nothing here feeds recovery behavior.
//
// Accumulation scope: unlike RecoveryMetrics (reset per Recover / per Parse), GrammarDiagnostics
// ACCUMULATES across parses for the lifetime of the Parser. The four quality signals are corpus-
// level ("anchor never used", "strict region never fires", "always the same S2 anchor") — they only
// make sense over the whole corpus of parses, not a single one. Reset() clears it for a fresh run.
//
// The four collection signals (6.3.2 turns these raw counts into author-facing findings):
//  (1) anchor usage        — how many times each anchor (by rule name) was used for an S2 resync;
//                            an anchor with count 0 was never used.
//  (2) recovery points     — the DISTINCT recovery points per rule (the top frame's Location at the
//                            recovery event); "all identical" == DistinctRecoveryPoints == 1.
//  (3) strict-region firing — how many times a Recoverable=false region fired (S1..S5 suppressed in
//                             Generate); count 0 == the region never fires on the corpus.
//  (4) S2 anchor usage     — the set of distinct anchors actually used for S2 resync; "always the
//                            same one" (redundant) == UsedAnchors.Count == 1.
// (1) and (4) share the anchor-usage data: the used anchors are exactly the keys of the count map.
public sealed class GrammarDiagnostics
{
    private readonly Dictionary<string, int> _anchorUsage = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<FrameLocation>> _recoveryPoints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _strictRegionFirings = new(StringComparer.Ordinal);

    // ============ observation hooks (called from the recovery sites; observation-only) ============

    // (1)/(4): an S2 resync candidate was generated for this anchor (T1 full-validation or T2 can-start).
    public void NoteAnchorUse(string anchorRuleName)
    {
        _anchorUsage.TryGetValue(anchorRuleName, out var count);
        _anchorUsage[anchorRuleName] = count + 1;
    }

    // (2): a recovery was accepted in this rule, at this top-frame location (a distinct recovery point).
    public void NoteRecoveryPoint(string ruleName, FrameLocation location)
    {
        if (!_recoveryPoints.TryGetValue(ruleName, out var set))
        {
            set = new HashSet<FrameLocation>();
            _recoveryPoints[ruleName] = set;
        }
        set.Add(location);
    }

    // (3): a failure inside this strict (Recoverable=false) region fired — S1..S5 were suppressed.
    public void NoteStrictRegionFiring(string ruleName)
    {
        _strictRegionFirings.TryGetValue(ruleName, out var count);
        _strictRegionFirings[ruleName] = count + 1;
    }

    // ============ public read surface (consumed by 6.3.2) ============

    // (1): usage count for one anchor; 0 if it was never used.
    public int AnchorUsage(string anchorRuleName) =>
        _anchorUsage.TryGetValue(anchorRuleName, out var count) ? count : 0;

    // (1): every USED anchor with its count (an anchor absent here was never used on the corpus).
    public IReadOnlyDictionary<string, int> AnchorUsageAll => _anchorUsage;

    // (4): the set of distinct anchors used for S2 resync ("always the same one" == Count == 1).
    public IReadOnlyCollection<string> UsedAnchors => _anchorUsage.Keys;

    // (2): number of DISTINCT recovery points for a rule; 0 if the rule never recovered.
    public int DistinctRecoveryPoints(string ruleName) =>
        _recoveryPoints.TryGetValue(ruleName, out var set) ? set.Count : 0;

    // (2): the distinct recovery points (top-frame Locations) recorded for a rule.
    public IReadOnlyCollection<FrameLocation> RecoveryPoints(string ruleName) =>
        _recoveryPoints.TryGetValue(ruleName, out var set) ? set : [];

    // (2): the rules that recovered at least once (the keys of the per-rule recovery-point map).
    public IReadOnlyCollection<string> RecoveredRules => _recoveryPoints.Keys;

    // (2): total distinct recovery points across all rules.
    public int TotalDistinctRecoveryPoints => _recoveryPoints.Values.Sum(set => set.Count);

    // (3): firing count for one strict region; 0 if it never fired on the corpus.
    public int StrictRegionFirings(string ruleName) =>
        _strictRegionFirings.TryGetValue(ruleName, out var count) ? count : 0;

    // (3): every strict region that fired at least once, with its count.
    public IReadOnlyDictionary<string, int> StrictRegionFiringsAll => _strictRegionFirings;

    // Clear all four signals (e.g. between corpus runs).
    public void Reset()
    {
        _anchorUsage.Clear();
        _recoveryPoints.Clear();
        _strictRegionFirings.Clear();
    }
}
