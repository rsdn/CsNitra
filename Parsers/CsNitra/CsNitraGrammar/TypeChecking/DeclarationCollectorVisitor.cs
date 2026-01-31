using CsNitra.Ast;

namespace CsNitra.TypeChecking;

internal sealed class DeclarationCollectorVisitor : AstVisitor
{
    private readonly TypeCheckingContext _context;
    private readonly List<PrecedenceDependency> _precedenceDependencies;

    public DeclarationCollectorVisitor(TypeCheckingContext context, List<PrecedenceDependency> precedenceDependencies)
    {
        _context = context;
        _precedenceDependencies = precedenceDependencies;
    }

    public override void Visit(PrecedenceStatementAst node)
    {
        _precedenceDependencies.Add(new PrecedenceDependency(
            node.Precedences.ToList(),
            new SourceSpan(node.StartPos, node.EndPos)
        ));
    }

    public override void Visit(RuleStatementAst node)
    {
        var symbol = new RuleSymbol(node.Name, _context.Source, node, null);
        _context.GlobalScope.AddSymbol(symbol);
    }

    public override void Visit(SimpleRuleStatementAst node)
    {
        var symbol = new RuleSymbol(node.Name, _context.Source, null, node);
        _context.GlobalScope.AddSymbol(symbol);
    }
}
