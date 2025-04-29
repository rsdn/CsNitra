namespace Regex;

public record DfaState(int Id, bool IsFinal)
{
    public List<DfaTransition> Transitions { get; } = new();
}

public record DfaTransition(RegexNode Condition, DfaState Target);
