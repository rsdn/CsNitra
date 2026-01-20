namespace ExtensibleParaser;

public abstract record Rule(string Kind)
{
    public abstract override string ToString();
    public virtual Rule InlineReferences(Dictionary<string, Rule> inlineableRules) => this;
    public abstract IEnumerable<Rule> GetSubRules<T>() where T : Rule;
}

public abstract record Terminal(string Kind) : Rule(Kind)
{
    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) => this;
    /// <summary>
    /// Пробует распознать (сопоставить) заданный литерал в позиции. Не пробует искать подстроку за пределами стартового символа,
    /// как классические регулярные вырвжения.
    /// </summary>
    /// <param name="input">Входная строка.</param>
    /// <param name="startPos">Позиция с которой производится распознование (сопоставление).</param>
    /// <returns>-1 - если распознование (сопоставление) не неудалось. Больше нуля, если распознавание удалось.
    /// 0 возвращается для регулярных выражений допускающих пусту строку, наприимер для "a*".</returns>
    public abstract int TryMatch(string input, int startPos);
    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
    }
}

public sealed record Literal(string Value, string? Kind = null) : Terminal(Kind ?? Value)
{
    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) => this;
    public override int TryMatch(string input, int startPos) =>
        input.AsSpan(startPos).StartsWith(Value, StringComparison.Ordinal) ? Value.Length : -1;
    public override string ToString() => $"«{Value}»";
    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
    }
}

public record Seq(Rule[] Elements, string Kind) : Rule(Kind)
{
    public override string ToString() => string.Join<Rule>(" ", Elements);
    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) =>
        new Seq(Elements.Select(e => e.InlineReferences(inlineableRules)).ToArray(), Kind);
    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var element in Elements)
            foreach (var subRule in element.GetSubRules<T>())
                yield return subRule;
    }
}

public record OneOrMany(Rule Element, string? Kind = null) : Rule(Kind ?? nameof(OneOrMany))
{
    public override string ToString() => $"{Element}+";
    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) =>
        new OneOrMany(Element.InlineReferences(inlineableRules), Kind);
    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var subRule in Element.GetSubRules<T>())
            yield return subRule;
    }
}

public record ZeroOrMany(Rule Element, string? Kind = null) : Rule(Kind ?? nameof(ZeroOrMany))
{
    public override string ToString() => $"{Element}*";
    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) =>
        new ZeroOrMany(Element.InlineReferences(inlineableRules), Kind);
    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var subRule in Element.GetSubRules<T>())
            yield return subRule;
    }
}

public record Optional(Rule Element, string? Kind = null) : Rule(Kind ?? nameof(Optional))
{
    public override string ToString() => $"{Element}?";
    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) =>
        new Optional(Element.InlineReferences(inlineableRules), Kind);
    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var subRule in Element.GetSubRules<T>())
            yield return subRule;
    }
}

public record OftenMissed(Rule Element, string Kind = "Error") : Rule(Kind)
{
    public override string ToString() => $"{Element}?";
    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) =>
        new OftenMissed(Element.InlineReferences(inlineableRules));
    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var subRule in Element.GetSubRules<T>())
            yield return subRule;
    }
}

public record Ref(string RuleName, string? Kind = null) : Rule(Kind ?? nameof(RuleName))
{
    public override string ToString() => RuleName;
    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) =>
        inlineableRules.TryGetValue(RuleName, out var rule) ? rule.InlineReferences(inlineableRules) : this;
    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
    }
}

public record ReqRef(string RuleName, int Precedence = 0, bool Right = false, string? Kind = null) : Ref(RuleName, Kind ?? nameof(RuleName))
{
    public override string ToString() => $"{RuleName} {{{Precedence}, {(Right ? "left" : "right")}}}";
    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) => this;
    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
    }
}

public record RuleWithPrecedence(string Kind, Seq Seq, int Precedence, bool Right);

public record TdoppRule(
    Ref Name,
    string Kind,
    Rule[] Prefix,
    RuleWithPrecedence[] Postfix,
    Rule[] RecoveryPrefix,
    RuleWithPrecedence[] RecoveryPostfix
) : Rule(Kind)
{
    public override string ToString() =>
        $"{Name} = Prefix: {string.Join<Rule>(" | ", Prefix)}" +
        $" Postfix: {string.Join("", Postfix.Select(x => $" {{{x.Precedence}{(x.Right ? "" : " right")}}} {x.Seq}"))}";
    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var prefix in Prefix)
            foreach (var subRule in prefix.GetSubRules<T>())
                yield return subRule;
        foreach (var postfix in Postfix)
            foreach (var subRule in postfix.Seq.GetSubRules<T>())
                yield return subRule;
        foreach (var recoveryPrefix in RecoveryPrefix)
            foreach (var subRule in recoveryPrefix.GetSubRules<T>())
                yield return subRule;
        foreach (var recoveryPostfix in RecoveryPostfix)
            foreach (var subRule in recoveryPostfix.Seq.GetSubRules<T>())
                yield return subRule;
    }
}

public record AndPredicate(Rule PredicateRule, Rule MainRule) : Rule("&")
{
    public override string ToString() => $"&{PredicateRule} {MainRule}";
    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) =>
        new AndPredicate(
            PredicateRule.InlineReferences(inlineableRules),
            MainRule.InlineReferences(inlineableRules));
    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var subRule in MainRule.GetSubRules<T>())
            yield return subRule;
    }
}

public record NotPredicate(Rule PredicateRule, Rule MainRule) : Rule("!")
{
    public override string ToString() => $"(!{PredicateRule} {MainRule})";
    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) =>
        new NotPredicate(
            PredicateRule.InlineReferences(inlineableRules),
            MainRule.InlineReferences(inlineableRules));
    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var subRule in PredicateRule.GetSubRules<T>())
            yield return subRule;
        foreach (var subRule in MainRule.GetSubRules<T>())
            yield return subRule;
    }
}

public record SeparatedList(
    Rule Element,
    Rule Separator,
    string Kind,
    SeparatorEndBehavior EndBehavior = SeparatorEndBehavior.Optional,
    bool CanBeEmpty = true)
    : Rule(Kind)
{
    public override string ToString() => $"({Element}; {Separator} {EndBehavior})*{(CanBeEmpty ? "" : "+")}";

    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules)
    {
        var inlinedElement = Element.InlineReferences(inlineableRules);
        var inlinedSeparator = Separator.InlineReferences(inlineableRules);
        return new SeparatedList(inlinedElement, inlinedSeparator, Kind, EndBehavior, CanBeEmpty);
    }

    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var subRule in Element.GetSubRules<T>())
            yield return subRule;

        // Мб. не требуется.
        foreach (var subRule in Separator.GetSubRules<T>())
            yield return subRule;
    }
}

public enum SeparatorEndBehavior
{
    /// <summary>
    /// опциональный - конечный разделитель может быть, а может не быть
    /// </summary>
    Optional,
    /// <summary>
    /// обязательный - разделитель должен быть в конце обязательно
    /// </summary>
    Required,
    /// <summary>
    /// запрещён - разделитель должен в конце обязательно отсутствовать
    /// </summary>
    Forbidden
}

public abstract record RecoveryTerminal(string Kind) : Terminal(Kind);

public record EmptyTerminal(string Kind) : RecoveryTerminal(Kind)
{
    public override int TryMatch(string input, int position) => 0;
    public override string ToString() => Kind;
}
