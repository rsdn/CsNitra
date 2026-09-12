namespace ExtensibleParser.Recovery;

public static class RecoveryStackReconstructor
{
    public static IReadOnlyList<ParseContext> ReconstructFromMemo(
        Dictionary<(int pos, string rule, int precedence), Result> memo,
        Dictionary<(int pos, string rule, int precedence), Result> partialMemo,
        int errorPos)
    {
        var partials = partialMemo
            .Where(kvp => kvp.Value.ResultKind == Result.Kind.Partial)
            .Select(kvp => kvp.Value)
            .Where(p => p.NewPos <= errorPos)
            .OrderBy(p => p.NewPos)
            .ThenByDescending(p => 0)
            .ToList();

        var stack = new List<ParseContext>();
        foreach (var partial in partials)
        {
            if (partial.Context != null)
                stack.Add(partial.Context);
        }

        var failedRules = memo
            .Where(kvp => kvp.Value.ResultKind == Result.Kind.Failure && kvp.Value.MaxFailPos == errorPos)
            .Select(kvp => new ParseContext(
                RuleName: kvp.Key.rule,
                Location: new SeqFrameLocation(0),
                Expected: [],
                Options: null))
            .ToList();

        stack.AddRange(failedRules);
        return stack;
    }
}
