using CsNitra.Ast;
using ExtensibleParser;

namespace CsNitra.TypeChecking;

public abstract class Symbol
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

public sealed class PrecedenceSymbol : Symbol
{
    public int BindingPower { get; }

    public PrecedenceSymbol(Identifier name, Source source, int bindingPower)
        : base(name, source)
    {
        BindingPower = bindingPower;
    }

    public override string ToString() => $"{Name.Value}={BindingPower}";
}

public sealed class PrecedenceDependency
{
    public IReadOnlyList<Identifier> Identifiers { get; }
    public SourceSpan Location { get; }

    public PrecedenceDependency(IReadOnlyList<Identifier> identifiers, SourceSpan location)
    {
        Identifiers = identifiers;
        Location = location;
    }
}

public sealed class RuleSymbol : Symbol
{
    public RuleStatementAst? RuleStatement { get; }
    public SimpleRuleStatementAst? SimpleRuleStatement { get; }

    public RuleSymbol(Identifier name, Source source, RuleStatementAst? ruleStatement, SimpleRuleStatementAst? simpleRuleStatement)
        : base(name, source)
    {
        RuleStatement = ruleStatement;
        SimpleRuleStatement = simpleRuleStatement;
    }

    public override string ToString() => $"Rule({Name.Value})";
}

public sealed class TerminalSymbol : Symbol
{
    public Terminal Terminal { get; }

    public TerminalSymbol(Identifier name, Source source, Terminal terminal)
        : base(name, source)
    {
        Terminal = terminal;
    }

    public override string ToString() => $"Terminal({Name.Value})";
}
