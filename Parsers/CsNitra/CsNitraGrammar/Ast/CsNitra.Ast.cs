namespace CsNitra.Ast;

public abstract partial record CsNitraAst(int StartPos, int EndPos)
{
    public CsNitraAst() : this(StartPos: 0, EndPos: 0) { }
    public int Length => EndPos - StartPos;
}

public sealed partial record GrammarAst(
    IReadOnlyList<UsingAst> Usings,
    IReadOnlyList<StatementAst> Statements,
    int StartPos,
    int EndPos
) : CsNitraAst(StartPos, EndPos)
{
    public override string ToString() =>
        $"Grammar(Usings: {Usings.Count}, Statements: {Statements.Count})";
}

public abstract partial record UsingAst(int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);

public sealed partial record OpenUsingAst(
    Literal UsingKw,
    QualifiedIdentifierAst QualifiedIdentifier,
    Literal Semicolon,
    int StartPos,
    int EndPos
) : UsingAst(StartPos, EndPos)
{
    public override string ToString() => $"using {string.Join(".", QualifiedIdentifier)};";
}

public sealed partial record AliasUsingAst(
    string Alias,
    QualifiedIdentifierAst QualifiedIdentifier,
    int StartPos,
    int EndPos
) : UsingAst(StartPos, EndPos)
{
    public override string ToString() => $"using {Alias} = {QualifiedIdentifier};";
}

public sealed partial record QualifiedIdentifierAst(
    IReadOnlyList<Identifier> Parts,
    IReadOnlyList<Literal> Delimiters,
    int StartPos,
    int EndPos
) : CsNitraAst(StartPos, EndPos)
{
    public override string ToString() => string.Join(".", Parts);
}

public abstract partial record StatementAst(int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);

public sealed partial record PrecedenceStatementAst(
    Literal PrecedenceKw,
    IReadOnlyList<Identifier> Precedences,
    Literal Semicolon,
    int StartPos,
    int EndPos
) : StatementAst(StartPos, EndPos)
{
    public override string ToString() => $"precedence {string.Join(", ", Precedences)};";
}

public sealed partial record RuleStatementAst(
    Identifier Name,
    Literal Eq,
    IReadOnlyList<AlternativeAst> Alternatives,
    Literal Semicolon,
    int StartPos,
    int EndPos
) : StatementAst(StartPos, EndPos)
{
    public override string ToString() =>
        $"""
        {Name} =
            {string.Join("\n    ", Alternatives.Select(a => $"| {a}"))};
        """;
}

public sealed partial record SimpleRuleStatementAst(
    Identifier Name,
    Literal Eq,
    RuleExpressionAst Expression,
    Literal Semicolon,
    int StartPos,
    int EndPos
) : StatementAst(StartPos, EndPos)
{
    public override string ToString() =>
        $"{Name} = {Expression};";
}

public abstract partial record AlternativeAst(int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);

public sealed partial record NamedAlternativeAst(
    Literal Pipe,
    Identifier Name,
    Literal Eq,
    RuleExpressionAst Expression,
    int StartPos,
    int EndPos
) : AlternativeAst(StartPos, EndPos)
{
    public override string ToString() => $$"""{{Name}} = {{{Expression}}}""";
}

public sealed partial record AnonymousAlternativeAst(
    Literal Pipe,
    QualifiedIdentifierAst RuleRef,
    int StartPos,
    int EndPos
) : AlternativeAst(StartPos, EndPos)
{
    public override string ToString() => RuleRef.ToString();
}

public abstract partial record RuleExpressionAst(int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);

public sealed partial record SequenceExpressionAst(
    RuleExpressionAst Left,
    RuleExpressionAst Right,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"{Left} {Right}";
}

public sealed partial record NamedExpressionAst(
    Identifier Name,
    Literal Eq,
    RuleExpressionAst Expression,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"{Name}=«{Expression}»";
}

public sealed partial record OptionalExpressionAst(
    RuleExpressionAst Expression,
    Literal Operator,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"{Expression}?";
}

public sealed partial record OftenMissedExpressionAst(
    RuleExpressionAst Expression,
    Literal Operator,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"{Expression}??";
}

public sealed partial record OneOrManyExpressionAst(
    RuleExpressionAst Expression,
    Literal Plus,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"{Expression}+";
}

public sealed partial record ZeroOrManyExpressionAst(
    RuleExpressionAst Expression,
    Literal Star,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"{Expression}*";
}

public sealed partial record AndPredicateExpressionAst(
    Literal Predicate,
    RuleExpressionAst Expression,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"&{Expression}";
}

public sealed partial record NotPredicateExpressionAst(
    Literal Predicate,
    RuleExpressionAst Expression,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"!{Expression}";
}

public sealed partial record LiteralAst(
    string Value,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"\"{Value}\"";
}

public sealed partial record RuleRefExpressionAst(
    QualifiedIdentifierAst Ref,
    PrecedenceAst? Precedence,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"{Ref}{Precedence}";
}

public sealed partial record GroupExpressionAst(
    Literal Open,
    RuleExpressionAst Expression,
    Literal Close,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"({Expression})";
}

public sealed partial record SeparatedListExpressionAst(
    RuleExpressionAst Element,
    RuleExpressionAst Separator,
    Literal? Modifier,
    Literal Count,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString()
    {
        var mod = Modifier != null ? $" : {Modifier}" : "";
        return $"({Element}; {Separator}{mod}){Count}";
    }
}

public partial record AssociativityAst(Literal Comma, Literal Associativity, int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos)
{
    public override string ToString() => $", right";
}


public partial record PrecedenceAst(Literal Colon, Identifier Precedence, AssociativityAst? Associativity, int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos)
{
    public override string ToString() => $" : {Precedence}{Associativity}";
}

public partial record Identifier(string Value, int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos)
{
    public override string ToString() => Value;
}

public partial record Literal(string Value, int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos)
{
    public override string ToString() => Value;
}

public abstract partial record Option(int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos)
{
    public Option() : this(StartPos: 0, EndPos: 0) { }

    public abstract bool IsSome { get; }
    public bool IsNone => !IsSome;

    public static Some<T> Some<T>(T value) where T : notnull, CsNitraAst => new(value);
    public static readonly None None = None.Create();
}

public sealed partial record Some<T>(T Value) : Option where T : notnull, CsNitraAst
{
    public override bool IsSome => true;
    public override string ToString() => $"Some({Value})";
}

public sealed partial record None : Option
{
    public override bool IsSome => false;
    public override string ToString() => "None()";

    internal static None Create() => new();
    private None() { }
}
