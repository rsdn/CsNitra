namespace ExtensibleParaser;

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

    private readonly Dictionary<string, Rule[]> _rules;
    private readonly Dictionary<string, HashSet<Terminal>> _firstSets;
    private readonly Dictionary<string, HashSet<Terminal>> _followSets = new();

    public FollowSetCalculator(Dictionary<string, Rule[]> rules)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _firstSets = ComputeFirstSets();
        ComputeFollowSets();
    }

    public HashSet<Terminal> GetFollowSet(string ruleName)
    {
        if (string.IsNullOrEmpty(ruleName))
            throw new ArgumentException("Rule name cannot be null or empty", nameof(ruleName));

        return _followSets.TryGetValue(ruleName, out var set) ? set : new HashSet<Terminal>();
    }

    private Dictionary<string, HashSet<Terminal>> ComputeFirstSets()
    {
        var first = new Dictionary<string, HashSet<Terminal>>();
        foreach (var ruleName in _rules.Keys)
        {
            first[ruleName] = ComputeFirstForRule(ruleName);
        }
        return first;
    }

    private HashSet<Terminal> ComputeFirstForRule(string ruleName)
    {
        var first = new HashSet<Terminal>();
        foreach (var rule in _rules[ruleName])
        {
            var flattened = FlattenRule(rule).ToList();
            foreach (var element in flattened)
            {
                if (element is Terminal term)
                {
                    first.Add(term);
                    break;
                }
                else if (element is Ref refRule)
                {
                    var refFirst = ComputeFirstForRule(refRule.RuleName);
                    first.UnionWith(refFirst);
                    if (!refFirst.Any(t => t is EmptyTerminal))
                        break;
                }
            }
        }
        return first;
    }

    private void ComputeFollowSets()
    {
        _followSets["Module"] = new HashSet<Terminal> { new EofTerminal() };

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
                            var firstBeta = ComputeFirstForSequence(beta);
                            var followA = _followSets.GetValueOrDefault(aRef.RuleName, new HashSet<Terminal>());

                            var beforeCount = followA.Count;
                            followA.UnionWith(firstBeta.Where(t => !(t is EmptyTerminal)));

                            if (firstBeta.Any(t => t is EmptyTerminal))
                                followA.UnionWith(_followSets.GetValueOrDefault(ruleName, new HashSet<Terminal>()));

                            if (followA.Count > beforeCount)
                            {
                                _followSets[aRef.RuleName] = followA;
                                changed = true;
                            }
                        }
                    }
                }
            }
        } while (changed);
    }

    private List<Terminal> ComputeFirstForSequence(List<Rule> sequence)
    {
        var result = new HashSet<Terminal>();
        for (int i = 0; i < sequence.Count; i++)
        {
            var rule = sequence[i];
            var ruleFirst = GetFirstForElement(rule);
            result.UnionWith(ruleFirst.Where(t => !(t is EmptyTerminal)));
            if (!ruleFirst.Any(t => t is EmptyTerminal))
                break;
            if (i == sequence.Count - 1)
                result.Add(new EmptyTerminal());
        }
        return result.ToList();
    }

    private HashSet<Terminal> GetFirstForElement(Rule element)
    {
        return element switch
        {
            Terminal t => new HashSet<Terminal> { t },
            Ref r => _firstSets[r.RuleName],
            _ => new HashSet<Terminal>()
        };
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

    private List<List<Rule>> ExpandRule(Rule rule)
    {
        return rule switch
        {
            Seq seq => new List<List<Rule>> { seq.Elements.SelectMany(FlattenRule).ToList() },
            Choice choice => choice.Alternatives.SelectMany(ExpandRule).ToList(),
            _ => new List<List<Rule>> { FlattenRule(rule).ToList() }
        };
    }

    private List<Rule> FlattenRule(Rule rule)
    {
        return rule switch
        {
            OneOrMany oom => FlattenRule(oom.Element),
            ZeroOrMany zom => FlattenRule(zom.Element),
            Optional opt => FlattenRule(opt.Element),
            _ => new List<Rule> { rule }
        };
    }
}