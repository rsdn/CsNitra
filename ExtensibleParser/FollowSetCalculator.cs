namespace ExtensibleParser;

using System;
using System.Collections.Generic;
using System.Linq;
using ExtensibleParser.Recovery;

public class FollowSetCalculator
{
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
                yield return EpsilonTerminal.Instance;
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

    // Упорядоченная агрегация терминаторов по кадрам снимка: от внутреннего (последний) к внешнему (stack[0]).
    // Терминаторы кадра = Options.Terminators ?? follow(правила кадра); дедупликация с сохранением порядка; EOF в конец.
    public Terminal[] GetTerminators(IReadOnlyList<StackFrame> stack)
    {
        var result = new List<Terminal>();
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            var frame = stack[i];
            var terminals = frame.Options?.Terminators ?? GetFollowSet(frame.RuleName).ToArray();
            foreach (var t in terminals)
                if (t is not EofTerminal && !result.Contains(t, TerminalComparer.Instance))
                    result.Add(t);
        }

        result.Add(EofTerminal.Instance);
        return result.ToArray();
    }

    private void ComputeFollowSets()
    {
        // Инициализация follow-set для стартовых символов
        var eof = EofTerminal.Instance;
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
                                if (t is not EpsilonTerminal)
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

    // Единый обход дерева правила с накоплением «что следует после» (first/nullable suffix).
    // Обрабатывает циклы, включая вложенные (цикл в теле цикла / в Seq в теле цикла).
    private void ProcessFollowSetForLoops(Rule rule, string parentRuleName)
    {
        if (rule is Seq seq)
            ProcessLoopSequence(seq.Elements, parentRuleName, ParentFollow(parentRuleName));
        else
            ProcessLoopNode(rule, parentRuleName, ParentFollow(parentRuleName));
    }

    // follow-set правила-контекста (что следует после всего правила)
    private List<Terminal> ParentFollow(string parentRuleName) =>
        _followSets.GetValueOrDefault(parentRuleName, new HashSet<Terminal>(TerminalEqualityComparer.Instance)).ToList();

    // Обход последовательности: для каждого элемента вычисляем «что следует после» и спускаемся в циклы/вложенные Seq.
    // Не рекурсируем в Ref-цели — каждое правило обрабатывается своим ходом во внешнем цикле.
    private void ProcessLoopSequence(Rule[] elements, string parentRuleName, List<Terminal> parentFollow)
    {
        for (int i = 0; i < elements.Length; i++)
        {
            var elem = elements[i];
            var whatFollows = WhatFollows(elements.Skip(i + 1).ToArray(), parentFollow);
            if (elem is ZeroOrMany or OneOrMany or SeparatedList)
                ProcessLoopNode(elem, parentRuleName, whatFollows);
            else if (elem is Seq s)
                ProcessLoopSequence(s.Elements, parentRuleName, whatFollows);
        }
    }

    // «Что следует после» последовательности after: first(after) ∪ (after nullable ? parentFollow : ∅)
    private List<Terminal> WhatFollows(Rule[] after, List<Terminal> parentFollow)
    {
        var (afterFirst, afterNullable) = ComputeFirstForSequence(after.ToList());
        var result = new List<Terminal>();
        foreach (var t in afterFirst)
            if (t is not EpsilonTerminal)
                result.Add(t);
        if (afterNullable)
            foreach (var t in parentFollow)
                if (!result.Contains(t, TerminalEqualityComparer.Instance))
                    result.Add(t);
        return result;
    }

    // Цикл: все Ref в теле получают first(тело) ∪ separator ∪ whatFollows; затем спуск во вложенные циклы тела.
    private void ProcessLoopNode(Rule loop, string parentRuleName, List<Terminal> whatFollows)
    {
        Terminal? separator = loop is SeparatedList sl ? sl.Separator as Terminal : null;

        Rule loopBody = loop switch
        {
            ZeroOrMany zom => zom.Element,
            OneOrMany oom => oom.Element,
            SeparatedList s => s.Element,
            _ => null
        };

        if (loopBody is null)
            return;

        var (bodyFirst, _) = ComputeFirst(loopBody);
        var bodyFirstList = bodyFirst.Where(t => t is not EpsilonTerminal).ToList();

        var refs = new HashSet<string>();
        foreach (var subRule in loopBody.GetSubRules<Ref>())
            if (subRule is Ref refRule && refRule.RuleName != null)
                refs.Add(refRule.RuleName);

        foreach (var refName in refs)
        {
            var followSet = _followSets.GetValueOrDefault(refName, null)
                ?? new HashSet<Terminal>(TerminalEqualityComparer.Instance);
            var beforeCount = followSet.Count;

            // Loop body first: another iteration of the same loop
            foreach (var t in bodyFirstList)
                followSet.Add(t);

            // Separator (for SeparatedList)
            if (separator is { } sep)
                followSet.Add(sep);

            // What comes after the loop
            foreach (var t in whatFollows)
                followSet.Add(t);

            if (followSet.Count > beforeCount)
                _followSets[refName] = followSet;
        }

        // Что следует после конца тела: следующая итерация (first тела) или конец цикла (whatFollows)
        var bodyFollow = new List<Terminal>(bodyFirstList);
        foreach (var t in whatFollows)
            if (!bodyFollow.Contains(t, TerminalEqualityComparer.Instance))
                bodyFollow.Add(t);

        if (loopBody is Seq nestedSeq)
            ProcessLoopSequence(nestedSeq.Elements, parentRuleName, bodyFollow);
        else if (loopBody is ZeroOrMany or OneOrMany or SeparatedList)
            ProcessLoopNode(loopBody, parentRuleName, bodyFollow);
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
                if (t is not EpsilonTerminal)
                    result.Add(t);
            }
            if (!ruleNullable)
            {
                allNullable = false;
                break;
            }
        }

        if (allNullable)
            result.Add(EpsilonTerminal.Instance);

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
