namespace ExtensibleParser;

using System;
using System.Collections.Generic;
using System.Linq;

public class FollowSetCalculator
{
    private record EmptyTerminal() : Terminal("ε")
    {
        public override int TryMatch(string input, int position) => 0;
    }

    private record EofTerminal() : Terminal("EOF")
    {
        public override int TryMatch(string input, int position) =>
            position >= input.Length ? 0 : -1;
    }

    // Terminal identity: Literal по Value, остальные по инстансу
    private sealed class TerminalEqualityComparer : IEqualityComparer<Terminal>
    {
        public static readonly TerminalEqualityComparer Instance = new();

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

    private readonly Dictionary<string, Rule[]> _rules;
    private readonly string[] _startSymbols;
    private readonly Dictionary<string, HashSet<Terminal>> _firstSets = new();
    private readonly Dictionary<string, HashSet<Terminal>> _followSets = new();
    private readonly Dictionary<string, bool> _nullableCache = new();
    private readonly HashSet<string> _computingFirst = new();
    private readonly HashSet<string> _computingNullable = new();

    public FollowSetCalculator(Dictionary<string, Rule[]> rules, params string[] startSymbols)
        : this(rules, (IEnumerable<string>)startSymbols)
    {
    }

    public FollowSetCalculator(Dictionary<string, Rule[]> rules, IEnumerable<string>? startSymbols = null)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _startSymbols = (startSymbols ?? new[] { "Module" }).ToArray();
        PopulateFirstSets();
        ComputeFollowSets();
    }

    private void PopulateFirstSets()
    {
        foreach (var ruleName in _rules.Keys)
        {
            _firstSets[ruleName] = new HashSet<Terminal>(ComputeFirstForRule(ruleName), TerminalEqualityComparer.Instance);
        }
    }

    public HashSet<Terminal> GetFollowSet(string ruleName)
    {
        if (string.IsNullOrEmpty(ruleName))
            throw new ArgumentException("Rule name cannot be null or empty", nameof(ruleName));

        return _followSets.TryGetValue(ruleName, out var set)
            ? new HashSet<Terminal>(set, TerminalEqualityComparer.Instance)
            : new HashSet<Terminal>();
    }

    public HashSet<Terminal> GetFirstSet(string ruleName)
    {
        if (string.IsNullOrEmpty(ruleName))
            throw new ArgumentException("Rule name cannot be null or empty", nameof(ruleName));

        return _firstSets.TryGetValue(ruleName, out var set)
            ? new HashSet<Terminal>(set, TerminalEqualityComparer.Instance)
            : new HashSet<Terminal>();
    }


    private IEnumerable<Terminal> ComputeFirstForRule(string ruleName)
    {
        foreach (var rule in _rules[ruleName])
        {
            var (terminals, nullable) = ComputeFirst(rule);
            foreach (var t in terminals)
                yield return t;
            if (nullable)
                yield return new EmptyTerminal();
        }
    }

    // Returns (terminals, isNullable) for a single rule
    private (IEnumerable<Terminal> Terminals, bool Nullable) ComputeFirst(Rule rule)
    {
        var terminals = new List<Terminal>();

        switch (rule)
        {
            case Literal l:
                return (new[] { l as Terminal }, false);

            case Terminal t:
                return (new[] { t }, false);

            case ReqRef rr:
            {
                var refFirst = GetFirstForRuleName(rr.RuleName);
                var refNullable = IsNullable(rr.RuleName);
                return (refFirst, refNullable);
            }

            case Ref r:
            {
                var refFirst = GetFirstForRuleName(r.RuleName);
                var refNullable = IsNullable(r.RuleName);
                return (refFirst, refNullable);
            }

            case Seq seq:
            {
                var seqTerminals = new List<Terminal>();
                bool allNullable = true;

                foreach (var element in seq.Elements)
                {
                    var (elemFirst, elemNullable) = ComputeFirst(element);
                    seqTerminals.AddRange(elemFirst);
                    if (!elemNullable)
                    {
                        allNullable = false;
                        break;
                    }
                }

                return (seqTerminals, allNullable);
            }

            case OneOrMany oom:
            {
                var (elemFirst, elemNullable) = ComputeFirst(oom.Element);
                return (elemFirst, elemNullable);
            }

            case ZeroOrMany zom:
            {
                var (elemFirst, _) = ComputeFirst(zom.Element);
                return (elemFirst, true);
            }

            case Optional opt:
            {
                var (elemFirst, _) = ComputeFirst(opt.Element);
                return (elemFirst, true);
            }

            case OftenMissed om:
            {
                var (elemFirst, _) = ComputeFirst(om.Element);
                return (elemFirst, true);
            }

            case SeparatedList sl:
            {
                var (elemFirst, _) = ComputeFirst(sl.Element);
                return (elemFirst, sl.CanBeEmpty);
            }

            case AndPredicate _:
                return (Array.Empty<Terminal>(), true);

            case NotPredicate _:
                return (Array.Empty<Terminal>(), true);

            default:
                return (Array.Empty<Terminal>(), false);
        }
    }

    private IEnumerable<Terminal> GetFirstForRuleName(string ruleName)
    {
        if (_firstSets.TryGetValue(ruleName, out var set))
            return set;

        if (_computingFirst.Contains(ruleName))
            return Array.Empty<Terminal>();

        _computingFirst.Add(ruleName);
        var result = new List<Terminal>();
        foreach (var terminal in ComputeFirstForRule(ruleName))
            result.Add(terminal);
        _computingFirst.Remove(ruleName);
        return result;
    }

   public bool IsNullable(string ruleName)
   {
        if (_nullableCache.TryGetValue(ruleName, out var cached))
            return cached;

        if (_computingNullable.Contains(ruleName))
            return false;

        _computingNullable.Add(ruleName);
        bool nullable = false;

        if (_rules.TryGetValue(ruleName, out var rules))
        {
            foreach (var rule in rules)
            {
                var (_, ruleNullable) = ComputeFirst(rule);
                if (ruleNullable)
                {
                    nullable = true;
                    break;
                }
            }
        }

        _computingNullable.Remove(ruleName);
        _nullableCache[ruleName] = nullable;
        return nullable;
    }

    private void ComputeFollowSets()
    {
        // Инициализация follow-set для стартовых символов
        var eof = new EofTerminal();
        foreach (var startSymbol in _startSymbols)
        {
            _followSets[startSymbol] = new HashSet<Terminal>(new[] { eof }, TerminalEqualityComparer.Instance);
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var ruleName in _rules.Keys)
            {
                foreach (var production in GetProductions(ruleName))
                {
                    var productionList = production.ToList();
                    for (int i = 0; i < productionList.Count; i++)
                    {
                        if (productionList[i] is Ref aRef)
                        {
                            var beta = productionList.Skip(i + 1).ToList();
                            var (firstBeta, betaNullable) = ComputeFirstForSequence(beta);
                            var followA = _followSets.GetValueOrDefault(aRef.RuleName, new HashSet<Terminal>(TerminalEqualityComparer.Instance));

                            var beforeCount = followA.Count;
                            foreach (var t in firstBeta)
                            {
                                if (t is not EmptyTerminal)
                                    followA.Add(t);
                            }

                            if (betaNullable)
                            {
                                var parentFollow = _followSets.GetValueOrDefault(ruleName, new HashSet<Terminal>(TerminalEqualityComparer.Instance));
                                foreach (var t in parentFollow)
                                    followA.Add(t);
                            }

                            if (followA.Count > beforeCount)
                            {
                                _followSets[aRef.RuleName] = followA;
                                changed = true;
                            }
                        }
                    }
                }

                // Additional pass: handle loops directly on raw rules
                foreach (var rule in _rules[ruleName])
                {
                    ProcessFollowSetForLoops(rule, ruleName);
                }
            }
        } while (changed);
    }

    // Process ZeroOrMany/OneOrMany/SeparatedList to add loop-follow contributions
    private void ProcessFollowSetForLoops(Rule rule, string parentRuleName)
    {
        if (rule is Seq seq)
        {
            for (int i = 0; i < seq.Elements.Length; i++)
            {
                ProcessFollowSetForLoopsInElement(seq.Elements[i], seq.Elements, i, seq.Elements.Length, parentRuleName);
            }
        }
        else
        {
            // Direct loop at rule level — nothing follows in production, so afterLoop = empty (nullable)
            ProcessFollowSetForLoopsDirect(rule, parentRuleName);
        }
    }

    private void ProcessFollowSetForLoopsDirect(Rule rule, string parentRuleName)
    {
        Terminal? separator = null;

        if (rule is SeparatedList sl)
            separator = sl.Separator as Terminal;

        Rule loopBody = rule switch
        {
            ZeroOrMany zom => zom.Element,
            OneOrMany oom => oom.Element,
            SeparatedList s => s.Element,
            _ => null
        };

        if (loopBody is null)
            return;

        var (loopBodyFirst, _) = ComputeFirst(loopBody);
        var parentFollow = _followSets.GetValueOrDefault(parentRuleName, new HashSet<Terminal>(TerminalEqualityComparer.Instance));

        ProcessFollowSetForRefsInRule(loopBody, loopBodyFirst, separator, parentFollow);
    }

    private void ProcessFollowSetForLoopsInElement(Rule elem, Rule[] siblings, int elemIndex, int siblingCount, string parentRuleName)
    {
        Terminal? separator = null;

        if (elem is SeparatedList sl2)
            separator = sl2.Separator as Terminal;

        Rule loopBody = elem switch
        {
            ZeroOrMany zom => zom.Element,
            OneOrMany oom => oom.Element,
            SeparatedList s2 => s2.Element,
            _ => null
        };

        if (loopBody is null)
        {
            // Recurse into nested Seq elements
            if (elem is Seq s)
            {
                for (int i = 0; i < s.Elements.Length; i++)
                {
                    ProcessFollowSetForLoopsInElement(s.Elements[i], siblings, elemIndex, siblingCount, parentRuleName);
                }
            }
            // Don't recurse into Ref targets — each rule is already processed by ProcessFollowSetForLoops
            // in the outer loop (foreach ruleName in _rules.Keys). Recursing here causes infinite
            // loops on mutually recursive rules (Block <-> Statement).
            return;
        }

        // What follows the loop in the parent sequence
        var afterLoop = new List<Rule>();
        for (int i = elemIndex + 1; i < siblingCount; i++)
            afterLoop.Add(siblings[i]);

        var (afterLoopFirst, afterLoopNullable) = ComputeFirstForSequence(afterLoop);

        var (loopBodyFirst, _) = ComputeFirst(loopBody);
        IEnumerable<Terminal> parentFollow = afterLoopNullable
            ? _followSets.GetValueOrDefault(parentRuleName, new HashSet<Terminal>(TerminalEqualityComparer.Instance))
            : Array.Empty<Terminal>();

        ProcessFollowSetForRefsInRule(loopBody, loopBodyFirst, separator, afterLoopFirst, parentFollow);
    }

    private void ProcessFollowSetForRefsInRule(
        Rule loopBody,
        IEnumerable<Terminal> loopBodyFirst,
        Terminal? separator,
        IEnumerable<Terminal> afterLoopFirst,
        IEnumerable<Terminal> parentFollow)
    {
        ProcessFollowSetForRefsInRuleInternal(loopBody, loopBodyFirst, separator, afterLoopFirst, parentFollow);
    }

    private void ProcessFollowSetForRefsInRule(
        Rule loopBody,
        IEnumerable<Terminal> loopBodyFirst,
        Terminal? separator,
        IEnumerable<Terminal> parentFollow)
    {
        ProcessFollowSetForRefsInRuleInternal(loopBody, loopBodyFirst, separator, Array.Empty<Terminal>(), parentFollow);
    }

    private void ProcessFollowSetForRefsInRuleInternal(
        Rule loopBody,
        IEnumerable<Terminal> loopBodyFirst,
        Terminal? separator,
        IEnumerable<Terminal> afterLoopFirst,
        IEnumerable<Terminal> parentFollow)
    {
        var refs = new HashSet<string>();
        foreach (var subRule in loopBody.GetSubRules<Ref>())
        {
            if (subRule is Ref refRule && refRule.RuleName != null)
                refs.Add(refRule.RuleName);
        }

        foreach (var refName in refs)
        {
            var followSet = _followSets.GetValueOrDefault(refName, null)
                ?? new HashSet<Terminal>(TerminalEqualityComparer.Instance);
            var beforeCount = followSet.Count;

            // Loop body first: another iteration of the same loop
            foreach (var t in loopBodyFirst)
            {
                if (t is not EmptyTerminal)
                    followSet.Add(t);
            }

            // Separator (for SeparatedList)
            if (separator is { } sep)
                followSet.Add(sep);

            // What comes after the loop
            foreach (var t in afterLoopFirst)
            {
                if (t is not EmptyTerminal)
                    followSet.Add(t);
            }

            // Parent follow-set (when loop/list is nullable)
            foreach (var t in parentFollow)
                followSet.Add(t);

            if (followSet.Count > beforeCount)
                _followSets[refName] = followSet;
        }
    }

    // Returns (terminals, isNullable) for a sequence of rules
    private (IEnumerable<Terminal> Terminals, bool Nullable) ComputeFirstForSequence(List<Rule> sequence)
    {
        var result = new List<Terminal>();
        bool allNullable = true;

        for (int i = 0; i < sequence.Count; i++)
        {
            var (ruleFirst, ruleNullable) = ComputeFirst(sequence[i]);
            foreach (var t in ruleFirst)
            {
                if (t is not EmptyTerminal)
                    result.Add(t);
            }
            if (!ruleNullable)
            {
                allNullable = false;
                break;
            }
        }

        if (allNullable)
            result.Add(new EmptyTerminal());

        return (result, allNullable);
    }

    private List<List<Rule>> GetProductions(string ruleName)
    {
        var productions = new List<List<Rule>>();
        foreach (var rule in _rules[ruleName])
        {
            foreach (var prod in ExpandRule(rule))
            {
                productions.Add(prod.ToList());
            }
        }
        return productions;
    }

    private List<List<Rule>> ExpandRule(Rule rule) => rule switch
    {
        Seq seq => new List<List<Rule>> { ExpandSeq(seq.Elements) },
        _ => new List<List<Rule>> { FlattenRule(rule) }
    };

    // Expand Seq elements while preserving loop semantics for follow-set computation
    private List<Rule> ExpandSeq(Rule[] elements)
    {
        var result = new List<Rule>();
        foreach (var element in elements)
        {
            result.AddRange(ExpandElement(element));
        }
        return result;
    }

    // Expand a single element, preserving Ref for follow-set computation
    private List<Rule> ExpandElement(Rule rule) => rule switch
    {
        Terminal t => new List<Rule> { t },
        ReqRef rr => new List<Rule> { new Ref(rr.RuleName) },
        Ref r => new List<Rule> { r },
        OneOrMany oom => ExpandElement(oom.Element),
        ZeroOrMany zom => ExpandElement(zom.Element),
        Optional opt => ExpandElement(opt.Element),
        OftenMissed om => ExpandElement(om.Element),
        SeparatedList sl => ExpandElement(sl.Element),
        Seq s => ExpandSeq(s.Elements),
        _ => new List<Rule> { rule }
    };

    private List<Rule> FlattenRule(Rule rule) => rule switch
    {
        OneOrMany oom => FlattenRule(oom.Element),
        ZeroOrMany zom => FlattenRule(zom.Element),
        Optional opt => FlattenRule(opt.Element),
        OftenMissed om => FlattenRule(om.Element),
        SeparatedList sl => FlattenRule(sl.Element),
        ReqRef rr => new List<Rule> { new Ref(rr.RuleName) },
        _ => new List<Rule> { rule }
    };
}
