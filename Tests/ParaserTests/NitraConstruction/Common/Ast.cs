#nullable enable

using ExtensibleParaser;

namespace NitraConstruction.Common;

public abstract record Ast
{
    public abstract override string ToString();
}

public record Number(int Value) : Expr
{
    public override string ToString() => Value.ToString();
}

public record Identifier(string Name) : Expr
{
    public override string ToString() => Name;
}

public record Token(string Value) : Ast
{
    public override string ToString() => Value;
}

public record CallExpr(Identifier Name, Args Arguments) : Expr
{
    public override string ToString() => $"Call: {Name}({Arguments})";
}

public record Args(IReadOnlyList<Expr> Arguments) : Ast
{
    public override string ToString() => string.Join(", ", Arguments);
}

public record Error(TerminalNode ErrorNode) : Expr
{
    public override string ToString() => $"«Error: expected Expr»";
}

public record UnexpectedOperator(TerminalNode ErrorNode) : Expr
{
    public override string ToString() => $"«Error: unexpected operator»";
}

public abstract record Expr : Ast;
