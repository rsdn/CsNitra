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

// 6.3.2: the kind of a grammar-quality finding. Each maps to one of the four collected signals
// (plus the "heavily used" refinement of signal 1).
public enum GrammarFindingKind
{
    // Signal 1 (negative): a declared anchor with usage 0 over the corpus.
    AnchorNeverUsed,
    // Signal 1 (refinement): an anchor used above the threshold — over-reliance on one resync target.
    AnchorHeavilyUsed,
    // Signal 2: a recovered rule whose distinct recovery points are exactly one (always the same point).
    RuleRecoveryPointsIdentical,
    // Signal 3 (negative): a declared Recoverable=false region with 0 firings over the corpus.
    StrictRegionNeverFires,
    // Signal 4: only one distinct S2 anchor is ever used (the declared anchor set is redundant).
    S2AnchorRedundant,
}

// 6.3.2: one author-facing grammar-quality finding. Name is the anchor / rule / region name the
// finding is about; Count is the relevant number (usage, firing, or distinct recovery points).
public sealed record GrammarFinding(GrammarFindingKind Kind, string Name, int Count)
{
    public override string ToString() => Kind switch
    {
        GrammarFindingKind.AnchorNeverUsed => $"anchor '{Name}' never used on the corpus",
        GrammarFindingKind.AnchorHeavilyUsed => $"anchor '{Name}' heavily used ({Count} uses)",
        GrammarFindingKind.RuleRecoveryPointsIdentical => $"rule '{Name}' always recovers at the same point",
        GrammarFindingKind.StrictRegionNeverFires => $"strict region '{Name}' never fires on the corpus",
        GrammarFindingKind.S2AnchorRedundant => $"only one S2 anchor is ever used ('{Name}', {Count} uses)",
        _ => $"{Kind}: {Name}",
    };
}

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

    // ============ 6.3.2: quality analysis — turn the collected corpus data into author-facing findings ============

    // Default threshold for the "anchor heavily used" finding: an anchor used MORE than this many times
    // over the accumulated corpus is flagged. This is a corpus-size-dependent heuristic (a large corpus
    // uses its anchors more), so it is exposed as a parameter on GetFindings and overridable per run.
    public const int DefaultHeavilyUsedThreshold = 10;

    // Produce the grammar-quality findings from the collected corpus data plus the grammar's DECLARED
    // anchors and strict regions. The declared sets are the baseline for the negative signals: an anchor
    // that is declared but used 0 times is "never used"; a strict region that is declared but fired 0
    // times is "never fires". (The declared sets come from DeclaredSets(parser) or are supplied directly.)
    public IReadOnlyList<GrammarFinding> GetFindings(
        IEnumerable<string> declaredAnchors,
        IEnumerable<string> declaredStrictRegions,
        int heavilyUsedThreshold = DefaultHeavilyUsedThreshold)
    {
        var findings = new List<GrammarFinding>();

        // Signal 1 (negative): a declared anchor with usage 0 over the corpus.
        foreach (var anchor in declaredAnchors.Distinct(StringComparer.Ordinal))
            if (AnchorUsage(anchor) == 0)
                findings.Add(new(GrammarFindingKind.AnchorNeverUsed, anchor, 0));

        // Signal 1 (refinement): an anchor used above the threshold (over-reliance on one resync target).
        foreach (var kvp in _anchorUsage)
            if (kvp.Value > heavilyUsedThreshold)
                findings.Add(new(GrammarFindingKind.AnchorHeavilyUsed, kvp.Key, kvp.Value));

        // Signal 2: a recovered rule with exactly one distinct recovery point (always the same point).
        foreach (var ruleName in _recoveryPoints.Keys)
            if (_recoveryPoints[ruleName].Count == 1)
                findings.Add(new(GrammarFindingKind.RuleRecoveryPointsIdentical, ruleName, 1));

        // Signal 3 (negative): a declared Recoverable=false region with 0 firings over the corpus.
        foreach (var region in declaredStrictRegions.Distinct(StringComparer.Ordinal))
            if (StrictRegionFirings(region) == 0)
                findings.Add(new(GrammarFindingKind.StrictRegionNeverFires, region, 0));

        // Signal 4: only one distinct S2 anchor is ever used (the declared anchor set is redundant).
        if (_anchorUsage.Count == 1)
        {
            var single = _anchorUsage.Single();
            findings.Add(new(GrammarFindingKind.S2AnchorRedundant, single.Key, single.Value));
        }

        return findings;
    }

    // Convenience: derive the declared anchor / strict-region sets from the grammar (parser.Rules) and
    // produce the findings in one call: parser.GrammarDiagnostics.GetFindings(parser).
    public IReadOnlyList<GrammarFinding> GetFindings(Parser parser, int heavilyUsedThreshold = DefaultHeavilyUsedThreshold)
    {
        var (anchors, strictRegions) = DeclaredSets(parser);
        return GetFindings(anchors, strictRegions, heavilyUsedThreshold);
    }

    // The DECLARED set of anchors and strict (Recoverable=false) regions in the grammar, extracted from
    // parser.Rules. An anchor is a Ref in a RecoveryRule.Options.Ancors — its name is Ref.RuleName, the
    // same name NoteAnchorUse records (so a declared anchor is "used" iff that name is in the usage map).
    // A strict region is a rule whose (any-depth) RecoveryRule has Options.Recoverable == false — its name
    // is the parser.Rules key, the same name NoteStrictRegionFiring records (for a top-level prefix, the
    // frame RuleName is exactly that key). These declared sets are the baseline for the never-used and
    // never-fires findings (a declared name absent from the usage/firing map has count 0).
    public static (IReadOnlyCollection<string> Anchors, IReadOnlyCollection<string> StrictRegions) DeclaredSets(Parser parser)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var strictRegions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kvp in parser.Rules)
        {
            var ruleName = kvp.Key;
            foreach (var alt in kvp.Value)
            {
                foreach (var rr in alt.GetSubRules<RecoveryRule>().OfType<RecoveryRule>())
                {
                    var options = rr.Options;
                    if (options is null)
                        continue;
                    if (options.Recoverable is false)
                        strictRegions.Add(ruleName);
                    if (options.Anchors is not null)
                        foreach (var a in options.Anchors)
                            if (a is Ref r)
                                anchors.Add(r.RuleName);
                }
            }
        }
        return (anchors, strictRegions);
    }

    // Clear all four signals (e.g. between corpus runs).
    public void Reset()
    {
        _anchorUsage.Clear();
        _recoveryPoints.Clear();
        _strictRegionFirings.Clear();
    }
}
