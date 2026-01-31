using CsNitra.Ast;

namespace CsNitra.TypeChecking;

internal sealed class DeclarationCollectorVisitor(
    TypeCheckingContext context,
    List<PrecedenceDependency> precedenceDependencies
) : AstVisitor
{
    public override void Visit(PrecedenceStatementAst node) =>
        precedenceDependencies.Add(new(node.Precedences.ToArray(), new(node.StartPos, node.EndPos)));

    public override void Visit(RuleStatementAst node) =>
        context.GlobalScope.AddSymbol(new RuleSymbol(node.Name, context.Source, node, SimpleRuleStatement: null));

    public override void Visit(SimpleRuleStatementAst node) =>
        context.GlobalScope.AddSymbol(new RuleSymbol(node.Name, context.Source, RuleStatement: null, node));
}
