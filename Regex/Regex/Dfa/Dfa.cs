namespace Regex;

public record DfaState(int Id, bool IsFinal)
{
    public List<DfaTransition> Transitions { get; } = new();
    public override string ToString() => $"{PrintStateId(this)} [{string.Join(", ", Transitions.Select(t => t.Target == this ? PrintStateId(t.Target) : DfaTransition.PrintTransition(t)))}]";

    public static string PrintStateId(DfaState s) => $"{(s.IsFinal ? "fq" : "q")}{s.Id}";
}

public record DfaTransition(RegexNode Condition, DfaState Target)
{
    public override string ToString() => PrintTransition(this);

    public static string PrintCondition(DfaTransition t) => t.Condition == null ? "ε" : t.Condition.ToString();
    public static string PrintTransition(DfaTransition t) => $"«{PrintCondition(t)}» → {DfaState.PrintStateId(t.Target)}";
}
