using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Diagnostics;

namespace ExtensibleParaser;

public class FollowSetCalculator
{
    public static readonly Terminal Epsilon = new EpsilonTerminal();
    public static readonly Terminal Eof = new EofTerminal();

    private readonly Dictionary<string, Rule[]> _rules;
    private readonly Dictionary<string, HashSet<Terminal>> _firstSets = new();
    private readonly Dictionary<string, HashSet<Terminal>> _followSets = new();
    private readonly Log? _log;
    private readonly string _startSymbol;

    public FollowSetCalculator(Dictionary<string, Rule[]> rules, string startSymbol = "Module", Log? log = null)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _startSymbol = startSymbol;
        _log = log;

        _log?.Info("Starting FIRST set computation.");
        ComputeFirstSets();
        LogSets(_firstSets, "FIRST");

        _log?.Info("Starting FOLLOW set computation.");
        ComputeFollowSets();
        LogSets(_followSets, "FOLLOW");
    }

    public HashSet<Terminal> GetFollowSet(string ruleName)
    {
        if (string.IsNullOrEmpty(ruleName))
            throw new ArgumentException("Rule name cannot be null or empty", nameof(ruleName));

        return _followSets.TryGetValue(ruleName, out var set) ? set : new HashSet<Terminal>(new TerminalComparer());
    }

    private void ComputeFirstSets()
    {
        foreach (var ruleName in _rules.Keys)
        {
            _firstSets[ruleName] = new HashSet<Terminal>(new TerminalComparer());
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var ruleName in _rules.Keys)
            {
                foreach (var production in _rules[ruleName])
                {
                    var firstOfProduction = ComputeFirstForSequence(production);
                    var currentSet = _firstSets[ruleName];
                    var initialCount = currentSet.Count;
                    currentSet.UnionWith(firstOfProduction);
                    if (currentSet.Count > initialCount)
                    {
                        changed = true;
                    }
                }
            }
        } while (changed);
    }

    private HashSet<Terminal> ComputeFirstForSequence(Rule rule)
    {
        var first = new HashSet<Terminal>(new TerminalComparer());
        var sequence = GetRuleSequence(rule);
        var nullable = true;

        foreach (var item in sequence)
        {
            var firstOfItem = GetFirstForElement(item);
            first.UnionWith(firstOfItem.Where(t => !t.Equals(Epsilon)));

            if (!firstOfItem.Contains(Epsilon))
            {
                nullable = false;
                break;
            }
        }

        if (nullable)
        {
            first.Add(Epsilon);
        }

        return first;
    }

    private IEnumerable<Rule> GetRuleSequence(Rule rule)
    {
        return rule switch
        {
            Seq s => s.Elements,
            _ => new[] { rule }
        };
    }


    private HashSet<Terminal> GetFirstForElement(Rule element)
    {
        return element switch
        {
            Terminal t => new HashSet<Terminal>(new[] { t }, new TerminalComparer()),
            Ref r => _firstSets[r.RuleName],
            ReqRef r => _firstSets[r.RuleName],
            OneOrMany o => GetFirstForElement(o.Element),
            ZeroOrMany z => new HashSet<Terminal>(GetFirstForElement(z.Element).Union(new[] { Epsilon }), new TerminalComparer()),
            Optional o => new HashSet<Terminal>(GetFirstForElement(o.Element).Union(new[] { Epsilon }), new TerminalComparer()),
            OftenMissed o => new HashSet<Terminal>(GetFirstForElement(o.Element).Union(new[] { Epsilon }), new TerminalComparer()),
            AndPredicate p => GetFirstForElement(p.MainRule),
            NotPredicate n => GetFirstForElement(n.MainRule),
            SeparatedList sl => sl.CanBeEmpty
                ? new HashSet<Terminal>(GetFirstForElement(sl.Element).Union(new[] { Epsilon }), new TerminalComparer())
                : GetFirstForElement(sl.Element),
            _ => new HashSet<Terminal>(new TerminalComparer())
        };
    }

    private void ComputeFollowSets()
    {
        foreach (var ruleName in _rules.Keys)
        {
            _followSets[ruleName] = new HashSet<Terminal>(new TerminalComparer());
        }

        if (_rules.ContainsKey(_startSymbol))
        {
            _followSets[_startSymbol].Add(Eof);
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var ruleName in _rules.Keys)
            {
                foreach (var production in _rules[ruleName])
                {
                    var sequence = GetRuleSequence(production).ToList();
                    for (int i = 0; i < sequence.Count; i++)
                    {
                        var currentRule = sequence[i];
                        if (currentRule is not Ref and not ReqRef) continue;

                        var currentRuleName = currentRule is Ref r ? r.RuleName : ((ReqRef)currentRule).RuleName;
                        if (!_followSets.ContainsKey(currentRuleName)) continue;

                        var followSet = _followSets[currentRuleName];
                        var initialCount = followSet.Count;

                        var restOfSequence = sequence.Skip(i + 1).ToList();
                        var firstOfRest = ComputeFirstForSequence(restOfSequence);

                        followSet.UnionWith(firstOfRest.Where(t => !t.Equals(Epsilon)));

                        if (firstOfRest.Contains(Epsilon))
                        {
                            followSet.UnionWith(_followSets[ruleName]);
                        }

                        if (followSet.Count > initialCount)
                        {
                            changed = true;
                        }
                    }
                }
            }
        } while (changed);
    }

    private HashSet<Terminal> ComputeFirstForSequence(IReadOnlyList<Rule> sequence)
    {
        var first = new HashSet<Terminal>(new TerminalComparer());
        var nullable = true;

        foreach (var item in sequence)
        {
            var firstOfItem = GetFirstForElement(item);
            first.UnionWith(firstOfItem.Where(t => !t.Equals(Epsilon)));

            if (!firstOfItem.Contains(Epsilon))
            {
                nullable = false;
                break;
            }
        }

        if (nullable)
        {
            first.Add(Epsilon);
        }

        return first;
    }


    private void LogSets(Dictionary<string, HashSet<Terminal>> sets, string setName)
    {
        if (_log == null) return;

        var sb = new StringBuilder();
        sb.AppendLine($"Computed {setName} sets:");
        foreach (var kvp in sets.OrderBy(kv => kv.Key))
        {
            sb.AppendLine($"  {kvp.Key}: {{ {string.Join(", ", kvp.Value.Select(t => t.ToString()))} }}");
        }
        _log.Info(sb.ToString());
    }

    private class TerminalComparer : IEqualityComparer<Terminal>
    {
        public bool Equals(Terminal x, Terminal y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return x.Kind == y.Kind;
        }

        public int GetHashCode(Terminal obj)
        {
            return obj.Kind.GetHashCode();
        }
    }
}

internal sealed record EpsilonTerminal() : Terminal("ε")
{
    public override int TryMatch(string input, int position) => 0;
    public override string ToString() => "ε";
}

internal sealed record EofTerminal() : Terminal("EOF")
{
    public override int TryMatch(string input, int position) =>
        position >= input.Length ? 0 : -1;
}
