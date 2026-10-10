using ExtensibleParser.Recovery;

namespace ExtensibleParser;

public partial class Parser
{
    private bool _clusterActive;
    private int _clusterEnd = -1;
    private int _clusterWork;
    private int _clusterWorkAtStart;
    private string? _clusterItemRule;

    private int ObservedRecoveryWork => Metrics.TotalAccept + Metrics.TotalRollback + Metrics.SpecCacheMisses;

    private void ResetCluster()
    {
        _clusterActive = false;
        _clusterEnd = -1;
        _clusterWork = 0;
        _clusterWorkAtStart = 0;
        _clusterItemRule = null;
    }

    private void BeginClusterPoint(int position, string input)
    {
        if (!Profile.EnableRecoveryClustering)
            return;
        var independent = _clusterItemRule is not null && _memo.Any(entry =>
            entry.Key.rule == _clusterItemRule && entry.Key.pos >= _clusterEnd &&
            entry.Value.TryGetSuccess(out var node, out var end) && end <= position &&
            end > entry.Key.pos && !ContainsRecovery(node));
        Log($"Cluster at {position}: end={_clusterEnd}, work={_clusterWork}, item={_clusterItemRule}, independent={independent}");
        if (_clusterEnd >= 0 && (position - _clusterEnd > Math.Max(0, Profile.ClusterDistance) || independent))
        {
            ResetCluster();
            Metrics.ClusterResets++;
        }
        // A known item prefix may still be an independent intact construct. Give it the normal
        // strategy path until a successful memo entry establishes whether the region is separate.
        var itemStart = _clusterItemRule is not null && FirstSets.Get(new Ref(_clusterItemRule), _followCalculator)
            .Any(t => t is not (EofTerminal or EpsilonTerminal) && t.TryMatch(input, position) > 0);
        _clusterActive = !itemStart && _clusterEnd >= 0 &&
            _clusterWork >= Math.Max(1, Profile.ClusterWorkThreshold);
        if (_clusterActive)
            Metrics.ClusteredPoints++;
        _clusterWorkAtStart = ObservedRecoveryWork;
    }

    private void AcceptClusterSkip(int position, FailureSnapshot? snapshot, RecoveryCandidate candidate)
    {
        if (!Profile.EnableRecoveryClustering || !candidate.Diagnostics.Any(d => d.StartPos < d.EndPos))
            return;
        _clusterEnd = Math.Max(candidate.Pos, candidate.Diagnostics.Max(d => d.EndPos));
        _clusterWork += ObservedRecoveryWork - _clusterWorkAtStart + 1;
        _clusterItemRule = snapshot?.Stack.LastOrDefault(f => f.Location is LoopFrameLocation).Location
            is LoopFrameLocation { Element: Ref item } ? item.RuleName : null;
    }

    private static bool ContainsRecovery(ISyntaxNode node) => node.IsRecovery || node switch
    {
        SeqNode seq => seq.RawElements.Any(ContainsRecovery),
        ListNode list => list.RawElements.Any(ContainsRecovery) || list.Delimiters.Any(ContainsRecovery),
        SomeNode some => ContainsRecovery(some.Value),
        _ => false
    };
}
