using CsNitra.Ast;
using ExtensibleParser;

namespace CsNitra.TypeChecking;

public abstract partial record Symbol
{
    public Identifier Name { get; }
    public Source Source { get; }
    public int StartPos => Name.StartPos;
    public int EndPos => Name.EndPos;
    public string Text => Name.Value;

    protected Symbol(Identifier name, Source source)
    {
        Name = name;
        Source = source;
    }
}

public sealed partial record PrecedenceSymbol(
    Identifier Name,
    Source Source,
    int BindingPower
) : Symbol(Name, Source)
{
    public override string ToString() => $"{Name.Value}={BindingPower}";
}

public sealed partial record PrecedenceDependency(
    IReadOnlyList<Identifier> Identifiers,
    SourceSpan Location
);

public sealed partial record RuleSymbol(
    Identifier Name,
    Source Source,
    RuleStatementAst? RuleStatement,
    SimpleRuleStatementAst? SimpleRuleStatement
) : Symbol(Name, Source)
{
    public override string ToString() => $"Rule({Name.Value})";
}

public sealed partial record TerminalSymbol(
    Identifier Name,
    Source Source,
    Terminal Terminal
) : Symbol(Name, Source)
{
    public override string ToString() => $"Terminal({Name.Value})";
}
