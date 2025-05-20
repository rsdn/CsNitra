namespace Regex;

public record NfaState(int Id)
{
    public List<NfaTransition> Transitions { get; } = new();
    public bool IsFinal { get; set; }

    public override string ToString() => $"{PrintStateId(this)} [{string.Join(", ", Transitions.Select(t => t.Target == this ? PrintStateId(t.Target) : NfaTransition.PrintTransition(t)))}]";

    public static string PrintStateId(NfaState s) => $"{(s.IsFinal ? "fs" : "s")}{s.Id}";
}

public record NfaTransition(NfaState Target, RegexNode? Condition = null)
{
    public override string ToString() => PrintTransition(this);

    public static string PrintCondition(NfaTransition t) => t.Condition == null ? "ε" : t.Condition.ToString();
    public static string PrintTransition(NfaTransition t) => $"«{PrintCondition(t)}» → {NfaState.PrintStateId(t.Target)}";
}
