namespace CsNitra;

public abstract record CsNitraAst(int StartPos, int EndPos)
{
    public CsNitraAst() : this(StartPos: 0, EndPos: 0) { }
    public int Length => EndPos - StartPos;
}

public sealed record GrammarAst(
    IReadOnlyList<UsingAst> Usings,
    IReadOnlyList<StatementAst> Statements,
    int StartPos,
    int EndPos
) : CsNitraAst(StartPos, EndPos)
{
    public override string ToString() =>
        $"Grammar(Usings: {Usings.Count}, Statements: {Statements.Count})";
}

public abstract record UsingAst(int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);

public sealed record OpenUsingAst(
    Literal UsingKw,
    IReadOnlyList<Identifier> QualifiedIdentifier,
    Literal Semicolon,
    int StartPos,
    int EndPos
) : UsingAst(StartPos, EndPos)
{
    public override string ToString() => $"using {string.Join(".", QualifiedIdentifier)};";
}

public sealed record AliasUsingAst(
    string Alias,
    QualifiedIdentifierAst QualifiedIdentifier,
    int StartPos,
    int EndPos
) : UsingAst(StartPos, EndPos)
{
    public override string ToString() => $"using {Alias} = {QualifiedIdentifier};";
}

public sealed record QualifiedIdentifierAst(
    IReadOnlyList<Identifier> Parts,
    int StartPos,
    int EndPos
) : CsNitraAst(StartPos, EndPos)
{
    public override string ToString() => string.Join(".", Parts);
}

public abstract record StatementAst(int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);

public sealed record PrecedenceStatementAst(
    Literal PrecedenceKw,
    IReadOnlyList<Identifier> Precedences,
    Literal Semicolon,
    int StartPos,
    int EndPos
) : StatementAst(StartPos, EndPos)
{
    public override string ToString() => $"precedence {string.Join(", ", Precedences)};";
}

public sealed record RuleStatementAst(
    Identifier Name,
    Literal Eq,
    IReadOnlyList<RuleAlternativeAst> Alternatives,
    int StartPos,
    int EndPos
) : StatementAst(StartPos, EndPos)
{
    public override string ToString() =>
        $"""
        {Name} =
            {string.Join("\n    ", Alternatives)};
        """;
}

public sealed record RuleAlternativeAst(
    Identifier Name,
    IReadOnlyList<RuleExpressionAst> SubRules,
    int StartPos,
    int EndPos
) : StatementAst(StartPos, EndPos)
{
    public override string ToString() =>
        $"| {Name} = {string.Join(" ", SubRules)};";
}

public abstract record RuleExpressionAst(int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);

public sealed record SequenceExpressionAst(
    RuleExpressionAst Left,
    RuleExpressionAst Right,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"{Left} {Right}";
}

public sealed record NamedExpressionAst(
    string Name,
    RuleExpressionAst Expression,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"{Name} = {Expression}";
}

public sealed record OptionalExpressionAst(
    RuleExpressionAst Expression,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"{Expression}?";
}

public sealed record OftenMissedExpressionAst(
    RuleExpressionAst Expression,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"{Expression}??";
}

public sealed record OneOrManyExpressionAst(
    RuleExpressionAst Expression,
    Literal Plus,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"{Expression}+";
}

public sealed record ZeroOrManyExpressionAst(
    RuleExpressionAst Expression,
    Literal Star,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"{Expression}*";
}

public sealed record AndPredicateExpressionAst(
    RuleExpressionAst Expression,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"&{Expression}";
}

public sealed record NotPredicateExpressionAst(
    RuleExpressionAst Expression,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"!{Expression}";
}

public abstract record LiteralAst(int StartPos, int EndPos) : RuleExpressionAst(StartPos, EndPos);

public sealed record StringLiteralAst(
    string Value,
    int StartPos,
    int EndPos
) : LiteralAst(StartPos, EndPos)
{
    public override string ToString() => $"\"{Value}\"";
}

public sealed record CharLiteralAst(
    string Value,
    int StartPos,
    int EndPos
) : LiteralAst(StartPos, EndPos)
{
    public override string ToString() => $"'{Value}'";
}

public sealed record RuleRefExpressionAst(
    QualifiedIdentifierAst Ref,
    Identifier? Precedence,
    Identifier? Associativity,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString()
    {
        var result = Ref.ToString();
        if (Precedence != null)
        {
            result += $" : {Precedence}";
            if (Associativity != null)
                result += $", {Associativity}";
        }
        return result;
    }
}

public sealed record GroupExpressionAst(
    RuleExpressionAst Expression,
    int StartPos,
    int EndPos
) : RuleExpressionAst(StartPos, EndPos)
{
    public override string ToString() => $"({Expression})";
}

public sealed record SeparatedListExpressionAst(
    RuleExpressionAst Element,
    RuleExpressionAst Separator,
    Literal? Modifier,
    string Count,
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

public record PrecedenceInfo(string Precedence, string? Associativity, int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);

public record Identifier(string Value, int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos)
{
    public override string ToString() => Value;
}

public record Literal(string Value, int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos)
{
    public override string ToString() => Value;
}

public abstract record Option(int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos)
{
    public Option() : this(StartPos: 0, EndPos: 0) { }

    public abstract bool IsSome { get; }
    public bool IsNone => !IsSome;

    public static Some<T> Some<T>(T value) where T : notnull => new(value);
    public static readonly None None = None.Create();
}

public sealed record Some<T>(T Value) : Option where T : notnull
{
    public override bool IsSome => true;
    public override string ToString() => $"Some({Value})";
}

public sealed record None : Option
{
    public override bool IsSome => false;
    public override string ToString() => "None()";

    internal static None Create() => new();
    private None() { }
}
