using CsNitra.Ast;

namespace CsNitra.TypeChecking;

internal sealed class DeclarationCollectorVisitor(
    TypeCheckingContext context,
    List<PrecedenceDependency> precedenceDependencies
) : AstVisitor
{
    public override void Visit(PrecedenceStatementAst node)
    {
        precedenceDependencies.Add(new PrecedenceDependency(
            node.Precedences.ToList(),
            new SourceSpan(node.StartPos, node.EndPos)
        ));
    }

    public override void Visit(RuleStatementAst node)
    {
        var symbol = new RuleSymbol(node.Name, context.Source, node, null);
        context.GlobalScope.AddSymbol(symbol);
    }

    public override void Visit(SimpleRuleStatementAst node)
    {
        var symbol = new RuleSymbol(node.Name, context.Source, null, node);
        context.GlobalScope.AddSymbol(symbol);
    }
}
