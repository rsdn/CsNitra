namespace ExtensibleParser.Recovery;

public static class FirstSets
{
    private sealed class TerminalEqualityComparer : IEqualityComparer<Terminal>
    {
        public bool Equals(Terminal? x, Terminal? y)
        {
            if (x is null || y is null)
                return x is null && y is null;

            if (x is Literal lx && y is Literal ly)
                return lx.Value == ly.Value;

            return ReferenceEquals(x, y);
        }

        public int GetHashCode(Terminal obj)
        {
            if (obj is Literal l)
                return l.Value.GetHashCode();
            return obj.GetHashCode();
        }
    }

    private static readonly TerminalEqualityComparer Comparer = new();

    // First-множество правила (только реальные терминалы; ε не представляется).
    public static Terminal[] Get(Rule rule, FollowSetCalculator? calculator = null)
    {
        var (terminals, _) = Compute(rule, calculator);
        return terminals;
    }

    public static bool IsNullable(Rule rule, FollowSetCalculator? calculator = null)
    {
        var (_, nullable) = Compute(rule, calculator);
        return nullable;
    }

    private static (Terminal[] Terminals, bool Nullable) Compute(Rule rule, FollowSetCalculator? calculator)
    {
        switch (rule)
        {
            case Terminal t:
                return ([t], false);

            case Seq seq:
            {
                var result = new List<Terminal>();
                var seen = new HashSet<Terminal>(Comparer);
                bool allNullable = true;

                foreach (var element in seq.Elements)
                {
                    var (elemFirst, elemNullable) = Compute(element, calculator);
                    AddAll(result, seen, elemFirst);
                    if (!elemNullable)
                    {
                        allNullable = false;
                        break;
                    }
                }

                return (result.ToArray(), allNullable);
            }

            case OneOrMany oneOrMany:
            {
                var (first, _) = Compute(oneOrMany.Element, calculator);
                return (first, false);
            }

            case ZeroOrMany or Optional or OftenMissed:
            {
                var element = rule switch
                {
                    ZeroOrMany z => z.Element,
                    Optional o => o.Element,
                    _ => ((OftenMissed)rule).Element
                };
                var (first, _) = Compute(element, calculator);
                return (first, true);
            }

            case AndPredicate or NotPredicate:
                return ([], true);

            case Ref or ReqRef:
            {
                var name = ((Ref)rule).RuleName;
                var first = calculator is null
                    ? []
                    : calculator.GetFirstSet(name).Where(t => t.Kind != "ε").ToArray();
                return (first, calculator is not null && calculator.IsNullable(name));
            }

            case SeparatedList separatedList:
            {
                if (separatedList.CanBeEmpty)
                    return ([], true);
                var (first, _) = Compute(separatedList.Element, calculator);
                return (first, false);
            }

            case TdoppRule tdopp:
            {
                var result = new List<Terminal>();
                var seen = new HashSet<Terminal>(Comparer);
                bool anyNullable = false;

                foreach (var prefix in tdopp.Prefix)
                {
                    var (first, nullable) = Compute(prefix, calculator);
                    AddAll(result, seen, first);
                    if (nullable)
                        anyNullable = true;
                }

                return (result.ToArray(), anyNullable);
            }

            default:
                return ([], false);
        }
    }

    private static void AddAll(List<Terminal> result, HashSet<Terminal> seen, Terminal[] terminals)
    {
        foreach (var t in terminals)
            if (seen.Add(t))
                result.Add(t);
    }
}
