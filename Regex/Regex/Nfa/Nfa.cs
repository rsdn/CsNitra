namespace Regex;

public record NfaState(int Id)
{
    public List<NfaTransition> Transitions { get; } = new();
    public bool IsFinal { get; set; }
}

public record NfaTransition(NfaState Target, RegexNode? Condition = null);
