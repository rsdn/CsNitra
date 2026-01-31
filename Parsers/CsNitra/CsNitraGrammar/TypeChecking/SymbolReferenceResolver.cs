using CsNitra.Ast;

namespace CsNitra.TypeChecking;

internal sealed class SymbolReferenceResolver : AstVisitor
{
    private readonly TypeCheckingContext _context;

    public SymbolReferenceResolver(TypeCheckingContext context)
    {
        _context = context;
    }

    public override void Visit(GrammarAst node)
    {
        foreach (var statement in node.Statements)
        {
            statement.Accept(this);
        }
    }

    public override void Visit(RuleStatementAst node)
    {
        node.Symbol = _context.FindRule(node.Name);
        foreach (var alternative in node.Alternatives)
        {
            alternative.Accept(this);
        }
    }

    public override void Visit(SimpleRuleStatementAst node)
    {
        node.Symbol = _context.FindRule(node.Name);
        node.Expression.Accept(this);
    }

    public override void Visit(NamedAlternativeAst node)
    {
        node.Expression.Accept(this);
    }

    public override void Visit(AnonymousAlternativeAst node)
    {
        if (node.RuleRef.Parts.Count == 1)
        {
            node.ReferencedSymbol = _context.FindRule(node.RuleRef.Parts[0]);
        }
    }

    public override void Visit(SequenceExpressionAst node)
    {
        node.Left.Accept(this);
        node.Right.Accept(this);
    }

    public override void Visit(NamedExpressionAst node)
    {
        node.Expression.Accept(this);
    }

    public override void Visit(OptionalExpressionAst node)
    {
        node.Expression.Accept(this);
    }

    public override void Visit(OftenMissedExpressionAst node)
    {
        node.Expression.Accept(this);
    }

    public override void Visit(OneOrManyExpressionAst node)
    {
        node.Expression.Accept(this);
    }

    public override void Visit(ZeroOrManyExpressionAst node)
    {
        node.Expression.Accept(this);
    }

    public override void Visit(AndPredicateExpressionAst node)
    {
        node.Expression.Accept(this);
    }

    public override void Visit(NotPredicateExpressionAst node)
    {
        node.Expression.Accept(this);
    }

    public override void Visit(GroupExpressionAst node)
    {
        node.Expression.Accept(this);
    }

    public override void Visit(SeparatedListExpressionAst node)
    {
        node.Element.Accept(this);
        node.Separator.Accept(this);
    }

    public override void Visit(RuleRefExpressionAst node)
    {
        if (node.Ref.Parts.Count == 1)
        {
            var identifier = node.Ref.Parts[0];
            node.ReferencedSymbol = _context.FindRule(identifier) ?? _context.FindTerminal(identifier) as Symbol;
        }

        if (node.Precedence != null)
        {
            node.PrecedenceSymbol = _context.FindPrecedence(node.Precedence.Precedence);
        }
    }
}
