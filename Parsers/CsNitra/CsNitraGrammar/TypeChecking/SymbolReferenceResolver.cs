using CsNitra.Ast;
using ExtensibleParser;

namespace CsNitra.TypeChecking;

internal sealed class SymbolReferenceResolver(TypeCheckingContext context) : AstVisitor
{
    public override void Visit(GrammarAst node)
    {
        foreach (var statement in node.Statements)
            statement.Accept(this);
    }

    public override void Visit(RuleStatementAst node)
    {
        node.Symbol = context.FindRule(node.Name);
        foreach (var alternative in node.Alternatives)
            alternative.Accept(this);
    }

    public override void Visit(SimpleRuleStatementAst node)
    {
        node.Symbol = context.FindRule(node.Name);
        node.Expression.Accept(this);
    }

    public override void Visit(NamedAlternativeAst node) => node.Expression.Accept(this);

    public override void Visit(AnonymousAlternativeAst node)
    {
        Guard.AreEqual(expected: 1, actual: node.RuleRef.Parts.Count);
        node.ReferencedSymbol = context.FindRule(node.RuleRef.Parts[0]);
    }

    public override void Visit(SequenceExpressionAst node)
    {
        node.Left.Accept(this);
        node.Right.Accept(this);
    }

    public override void Visit(NamedExpressionAst node) => node.Expression.Accept(this);

    public override void Visit(OptionalExpressionAst node) => node.Expression.Accept(this);

    public override void Visit(OftenMissedExpressionAst node) => node.Expression.Accept(this);

    public override void Visit(OneOrManyExpressionAst node) => node.Expression.Accept(this);

    public override void Visit(ZeroOrManyExpressionAst node) => node.Expression.Accept(this);

    public override void Visit(AndPredicateExpressionAst node) => node.Expression.Accept(this);

    public override void Visit(NotPredicateExpressionAst node) => node.Expression.Accept(this);

    public override void Visit(GroupExpressionAst node) => node.Expression.Accept(this);

    public override void Visit(SeparatedListExpressionAst node)
    {
        node.Element.Accept(this);
        node.Separator.Accept(this);
    }

    public override void Visit(RuleRefExpressionAst node)
    {
        Guard.AreEqual(expected: 1, actual: node.Ref.Parts.Count);

        var identifier = node.Ref.Parts[0];
        node.ReferencedSymbol = context.FindRule(identifier) ?? context.FindTerminal(identifier) as Symbol;

        if (node.Precedence != null)
            node.PrecedenceSymbol = context.FindPrecedence(node.Precedence.Precedence);
    }
}
