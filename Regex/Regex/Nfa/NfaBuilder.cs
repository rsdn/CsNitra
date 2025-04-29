namespace Regex;

public class NfaBuilder
{
    private int _stateId = 0;

    public (NfaState StartState, NfaState EndState) Build(RegexNode node)
    {
        _stateId = 0;
        var result = BuildNode(node);
        result.EndState.IsFinal = true;
        return result;
    }

    private (NfaState StartState, NfaState EndState) BuildNode(RegexNode node) => node switch
    {
        RegexChar c => BuildSingle(c),
        RegexAnyChar => BuildSingle(new RegexAnyChar()),
        RegexCharClass cc => BuildSingle(cc),
        RegexConcat c => BuildConcat(c),
        RegexAlternation a => BuildAlternation(a),
        RegexStar s => BuildStar(s),
        RegexPlus p => BuildPlus(p),
        RegexOptional o => BuildOptional(o),
        RegexGroup g => BuildNode(g.Node),
        _ => throw new NotSupportedException()
    };

    private (NfaState StartState, NfaState EndState) BuildSingle(RegexNode node)
    {
        var start = CreateState();
        var end = CreateState();
        start.Transitions.Add(new NfaTransition(end, node));
        return (start, end);
    }

    private (NfaState StartState, NfaState EndState) BuildConcat(RegexConcat concat)
    {
        if (concat.Nodes.Count == 0) return (CreateState(), CreateState());
        var (currentStart, currentEnd) = BuildNode(concat.Nodes[0]);
        foreach (var node in concat.Nodes.Skip(1))
        {
            var (nextStart, nextEnd) = BuildNode(node);
            currentEnd.Transitions.Add(new NfaTransition(nextStart));
            currentEnd = nextEnd;
        }
        return (currentStart, currentEnd);
    }

    private (NfaState StartState, NfaState EndState) BuildAlternation(RegexAlternation alt)
    {
        var start = CreateState();
        var end = CreateState();
        foreach (var node in alt.Nodes)
        {
            var (subStart, subEnd) = BuildNode(node);
            start.Transitions.Add(new NfaTransition(subStart));
            subEnd.Transitions.Add(new NfaTransition(end));
        }
        return (start, end);
    }

    private (NfaState StartState, NfaState EndState) BuildStar(RegexStar star)
    {
        var (subStart, subEnd) = BuildNode(star.Node);
        var start = CreateState();
        var end = CreateState();
        start.Transitions.AddRange(new[]
        {
            new NfaTransition(Target: subStart),
            new NfaTransition(Target: end)
        });
        subEnd.Transitions.AddRange(new[]
        {
            new NfaTransition(Target: subStart),
            new NfaTransition(Target: end)
        });
        return (start, end);
    }

    private (NfaState StartState, NfaState EndState) BuildPlus(RegexPlus plus)
    {
        var (subStart, subEnd) = BuildNode(plus.Node);
        var end = CreateState();

        // Петля для повторений: после первого прохождения возвращаемся к subStart
        subEnd.Transitions.Add(new NfaTransition(Target: subStart));

        // Финальный переход в end
        subEnd.Transitions.Add(new NfaTransition(Target: end));

        return (subStart, end);
    }

    private (NfaState StartState, NfaState EndState) BuildOptional(RegexOptional opt)
    {
        var (subStart, subEnd) = BuildNode(opt.Node);
        var start = CreateState();
        start.Transitions.AddRange(new[]
        {
            new NfaTransition(subStart),
            new NfaTransition(subEnd)
        });
        return (start, subEnd);
    }

    private NfaState CreateState() => new NfaState(_stateId++);
    private NfaState CreateFinalState() => new NfaState(_stateId++) { IsFinal = true };
}
