#nullable enable

namespace CsNitra;

public abstract record CsNitraAst(int StartPos, int EndPos);

// Grammar
public sealed record Grammar(
    int StartPos,
    int EndPos,
    IReadOnlyList<Using> Usings,
    IReadOnlyList<Statement> Statements
) : CsNitraAst(StartPos, EndPos);

public abstract record Statement(int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);
public sealed record RuleStatement(Rule Rule) : Statement(Rule.StartPos, Rule.EndPos);
public sealed record PrecedenceStatement(PrecedenceDecl Precedence) : Statement(Precedence.StartPos, Precedence.EndPos);

// Using
public abstract record Using(int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);
public sealed record OpenUsing(
    int StartPos,
    int EndPos,
    QualifiedIdentifier Namespace
) : Using(StartPos, EndPos);

public sealed record AliasUsing(
    int StartPos,
    int EndPos,
    string Alias,
    QualifiedIdentifier Target
) : Using(StartPos, EndPos);

public sealed record QualifiedIdentifier(
    int StartPos,
    int EndPos,
    IReadOnlyList<(string Name, int StartPos, int EndPos)> Parts
) : CsNitraAst(StartPos, EndPos);

// Precedence
public sealed record PrecedenceDecl(
    int StartPos,
    int EndPos,
    string Name,
    (int StartPos, int EndPos) NamePos,
    IReadOnlyList<Alternative> Alternatives
) : CsNitraAst(StartPos, EndPos);

// Rule
public sealed record Rule(
    int StartPos,
    int EndPos,
    string Name,
    (int StartPos, int EndPos) NamePos,
    IReadOnlyList<Alternative> Alternatives
) : CsNitraAst(StartPos, EndPos);

public abstract record Alternative(int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);
public sealed record SimpleAlternative(
    int StartPos,
    int EndPos,
    RuleExpression Expression
) : Alternative(StartPos, EndPos);

public sealed record NamedAlternative(
    int StartPos,
    int EndPos,
    string? Name,
    (int StartPos, int EndPos)? NamePos,
    IReadOnlyList<RuleExpression> Expressions
) : Alternative(StartPos, EndPos);

// RuleExpression
public abstract record RuleExpression(int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);

// Литералы
public sealed record LiteralExpression(
    int StartPos,
    int EndPos,
    string Value,
    string RawValue
) : RuleExpression(StartPos, EndPos);

// Ссылки
public sealed record RuleRefExpression(
    int StartPos,
    int EndPos,
    QualifiedIdentifier Ref,
    string? Precedence,
    (int StartPos, int EndPos)? PrecedencePos,
    string? Associativity,
    (int StartPos, int EndPos)? AssociativityPos
) : RuleExpression(StartPos, EndPos);

// Группа
public sealed record GroupExpression(
    int StartPos,
    int EndPos,
    IReadOnlyList<Alternative> Alternatives
) : RuleExpression(StartPos, EndPos);

// Последовательность
public sealed record SequenceExpression(
    int StartPos,
    int EndPos,
    RuleExpression Left,
    RuleExpression Right,
    string PrecedenceLevel
) : RuleExpression(StartPos, EndPos);

// Присвоение имени
public sealed record NamedExpression(
    int StartPos,
    int EndPos,
    string Name,
    (int StartPos, int EndPos) NamePos,
    RuleExpression Expression,
    string PrecedenceLevel
) : RuleExpression(StartPos, EndPos);

// Унарные операторы (префиксные)
public sealed record UnaryPrefixExpression(
    int StartPos,
    int EndPos,
    string Operator,
    RuleExpression Expression,
    string PrecedenceLevel
) : RuleExpression(StartPos, EndPos);

// Унарные операторы (постфиксные)
public sealed record UnaryPostfixExpression(
    int StartPos,
    int EndPos,
    string Operator,
    RuleExpression Expression,
    string PrecedenceLevel
) : RuleExpression(StartPos, EndPos);

// SeparatedList
public sealed record SeparatedListExpression(
    int StartPos,
    int EndPos,
    RuleExpression Element,
    RuleExpression Separator,
    string? Modifier,
    (int StartPos, int EndPos)? ModifierPos,
    string Count,
    (int StartPos, int EndPos) CountPos
) : RuleExpression(StartPos, EndPos);
