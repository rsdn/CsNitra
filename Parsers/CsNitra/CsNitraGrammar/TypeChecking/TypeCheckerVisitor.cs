using CsNitra.Ast;
using ExtensibleParser;

namespace CsNitra.TypeChecking;

internal sealed class TypeCheckerVisitor(TypeCheckingContext context) : AstVisitor
{
    public override void Visit(GrammarAst node)
    {
        // This method is called after declaration collection
        foreach (var statement in node.Statements)
            if (statement is RuleStatementAst or SimpleRuleStatementAst)
                statement.Accept(this);
    }

    public override void Visit(RuleStatementAst node)
    {
        if (context.FindRule(node.Name) == null)
        {
            context.ReportError($"Rule '{node.Name}' not found in symbol table", node);
            return;
        }

        using var _ = context.EnterScope(node);

        foreach (var alternative in node.Alternatives)
            alternative.Accept(this);
    }

    public override void Visit(SimpleRuleStatementAst node)
    {
        if (context.FindRule(node.Name) == null)
        {
            context.ReportError($"Rule '{node.Name}' not found in symbol table", node);
            return;
        }

        node.Expression.Accept(this);
    }

    public override void Visit(NamedAlternativeAst node)
    {
        using (context.EnterScope(node))
            node.Expression.Accept(this);
    }

    public override void Visit(AnonymousAlternativeAst node)
    {
        Guard.IsTrue(node.RuleRef.Parts.Count == 1);

        var identifier = node.RuleRef.Parts[0];
        if (context.FindRule(identifier) == null && context.FindTerminal(identifier) == null)
            context.ReportError($"Symbol '{identifier}' not found", node);
    }

    public override void Visit(FlattenSequenceExpressionAst node)
    {
        foreach (var e in node.Elements)
            e.Accept(this);
    }

    public override void Visit(SequenceExpressionAst node) =>
        throw new NotImplementedException("SequenceExpressionAst should have been flattened by FlattenSequenceExpressionAst");

    public override void Visit(RuleRefExpressionAst node)
    {
        Guard.AreEqual(expected: 1, actual: node.Ref.Parts.Count);

        var identifier = node.Ref.Parts[0];
        if ((node.ReferencedSymbol = context.FindRule(identifier)) is null && (node.ReferencedSymbol = context.FindTerminal(identifier)) is null)
            context.ReportError($"Symbol '{identifier}' not found", identifier);

        if (node.Precedence != null && (node.PrecedenceSymbol = context.FindPrecedence(node.Precedence.Precedence)) is null)
            context.ReportError($"Precedence '{node.Precedence.Precedence}' not found", node.Precedence);
    }
}
