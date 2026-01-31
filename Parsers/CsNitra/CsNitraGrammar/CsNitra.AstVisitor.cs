namespace CsNitra.Ast;

public interface IAstVisitor
{
    void Visit(GrammarAst node);
    void Visit(OpenUsingAst node);
    void Visit(AliasUsingAst node);
    void Visit(QualifiedIdentifierAst node);
    void Visit(PrecedenceStatementAst node);
    void Visit(RuleStatementAst node);
    void Visit(SimpleRuleStatementAst node);
    void Visit(NamedAlternativeAst node);
    void Visit(AnonymousAlternativeAst node);
    void Visit(SequenceExpressionAst node);
    void Visit(NamedExpressionAst node);
    void Visit(OptionalExpressionAst node);
    void Visit(OftenMissedExpressionAst node);
    void Visit(OneOrManyExpressionAst node);
    void Visit(ZeroOrManyExpressionAst node);
    void Visit(AndPredicateExpressionAst node);
    void Visit(NotPredicateExpressionAst node);
    void Visit(LiteralAst node);
    void Visit(RuleRefExpressionAst node);
    void Visit(GroupExpressionAst node);
    void Visit(SeparatedListExpressionAst node);
    void Visit(AssociativityAst node);
    void Visit(PrecedenceAst node);
    void Visit(Identifier node);
    void Visit(Literal node);
}

public abstract partial record CsNitraAst
{
    public abstract void Accept(IAstVisitor visitor);
}

public sealed partial record GrammarAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public abstract partial record UsingAst
{
    public abstract override void Accept(IAstVisitor visitor);
}

public sealed partial record OpenUsingAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record AliasUsingAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record QualifiedIdentifierAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public abstract partial record StatementAst
{
    public abstract override void Accept(IAstVisitor visitor);
}

public sealed partial record PrecedenceStatementAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record RuleStatementAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record SimpleRuleStatementAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public abstract partial record AlternativeAst
{
    public abstract override void Accept(IAstVisitor visitor);
}

public sealed partial record NamedAlternativeAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record AnonymousAlternativeAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public abstract partial record RuleExpressionAst
{
    public abstract override void Accept(IAstVisitor visitor);
}

public sealed partial record SequenceExpressionAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record NamedExpressionAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record OptionalExpressionAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record OftenMissedExpressionAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record OneOrManyExpressionAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record ZeroOrManyExpressionAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record AndPredicateExpressionAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record NotPredicateExpressionAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record LiteralAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record RuleRefExpressionAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record GroupExpressionAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record SeparatedListExpressionAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record AssociativityAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public sealed partial record PrecedenceAst
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public partial record Identifier
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public partial record Literal
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public abstract partial record Option
{
    public abstract override void Accept(IAstVisitor visitor);
}

public sealed partial record Some<T>
{
    public override void Accept(IAstVisitor visitor)
    {
        Value.Accept(visitor);
    }
}

public sealed partial record None
{
    public override void Accept(IAstVisitor visitor)
    {
    }
}
