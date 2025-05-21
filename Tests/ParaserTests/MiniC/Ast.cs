#nullable enable

using ExtensibleParaser;

namespace MiniC;

public abstract record Ast
{
    public abstract override string ToString();
}

public record VarDecl(Token Type, Identifier Name) : Ast
{
    public override string ToString() => $"VarDecl: {Name}";
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

public record BinaryExpr(string Op, Expr Left, Expr Right) : Expr
{
    public override string ToString() => $"({Left} {Op} {Right})";
}

public record UnaryExpr(string Op, Expr Expr) : Expr
{
    public override string ToString() => $"({Op}{Expr})";
}

public record Error(TerminalNode ErrorNode) : Expr
{
    public override string ToString() => $"«Error: expected Expr»";
}

public record UnexpectedOperator(TerminalNode ErrorNode) : Expr
{
    public override string ToString() => $"«Error: unexpected operator»";
}

public record ExprStmt(Expr Expr) : Ast
{
    public override string ToString() => $"ExprStmt: {Expr}";
}

public record FunctionDecl(Identifier Name, IReadOnlyList<Identifier> Parameters, Block Body) : Ast
{
    public override string ToString() =>
        $"FunctionDecl: {Name}({string.Join(", ", Parameters)}) {Body}";
}

public record CallExpr(Identifier Name, Args Arguments) : Expr
{
    public override string ToString() => $"Call: {Name}({Arguments})";
}

public record Args(IReadOnlyList<Expr> Arguments) : Ast
{
    public override string ToString() => string.Join(", ", Arguments);
}

public record Params(IReadOnlyList<Identifier> Parameters) : Ast
{
    public override string ToString() => $"Params: ({string.Join(", ", Parameters)})";
}

public record Block(IReadOnlyList<Ast> Statements, bool HasBraces = false) : Ast
{
    public override string ToString()
    {
        if (Statements.Count == 0)
            return HasBraces ? "{ }" : string.Empty;

        var content = string.Join("; ", Statements);
        return HasBraces ? $"{{ {content} }}" : content;
    }
}

public record IfStatement(Expr Condition, Block Then, Block? Else) : Ast
{
    public override string ToString() =>
        Else == null
            ? $"IfStmt: {Condition} then {Then}"
            : $"IfStmt: {Condition} then {Then} else {Else}";
}

public record ReturnStmt(Expr Value) : Ast
{
    public override string ToString() => $"Return({Value})";
}

public abstract record Expr : Ast;
