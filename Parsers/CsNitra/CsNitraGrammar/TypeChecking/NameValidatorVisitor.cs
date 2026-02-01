using CsNitra.Ast;

namespace CsNitra.TypeChecking;

internal sealed class NameValidatorVisitor(TypeCheckingContext context) : AstVisitor
{
    private readonly TypeCheckingContext _context = context;

    public override void Visit(RuleStatementAst node)
    {
        using (_context.EnterScope(node))
            foreach (var alternative in node.Alternatives)
                alternative.Accept(this);
    }

    public override void Visit(SimpleRuleStatementAst node)
    {
        using (_context.EnterScope(node))
            ValidateExpressionNames(node.Expression, node.Name.Value);
    }

    public override void Visit(NamedAlternativeAst node)
    {
        using (_context.EnterScope(node))
            ValidateExpressionNames(node.Expression, node.Name.Value);
    }

    public override void Visit(AnonymousAlternativeAst node)
    {
        // AnonymousAlternativeAst already has name from RuleRef
        // Verify that RuleRef exists
        if (node.RuleRef.Parts.Count == 1)
        {
            var identifier = node.RuleRef.Parts[0];
            if (_context.FindRule(identifier) == null
            && _context.FindTerminal(identifier) == null)
                _context.ReportError($"Symbol '{identifier}' not found", node);
        }
    }

    /// <summary>
    /// Validates that all subrules in expression have inferrable names.
    /// </summary>
    private void ValidateExpressionNames(RuleExpressionAst expression, string context)
    {
        var subruleNames = Naming.CollectAllSubruleNames(expression);

        foreach (var kvp in subruleNames)
        {
            var subrule = kvp.Key;
            var name = kvp.Value;

            if (name == null)
            {
                // Special handling for SequenceExpressionAst
                if (subrule is SequenceExpressionAst)
                    _context.ReportError(
                        $"Sequence expression must have explicit names for all elements. " +
                        $"Use 'Name=Expression' syntax for: {subrule}",
                        subrule);
                else
                    _context.ReportError(
                        $"Cannot infer name for subrule in {context}: {subrule}. " +
                        $"Use explicit naming with 'Name=' syntax.",
                        subrule);
            }
        }

        // Also recursively validate nested expressions
        switch (expression)
        {
            case SequenceExpressionAst seq:
                ValidateExpressionNames(seq.Left, $"{context}.Left");
                ValidateExpressionNames(seq.Right, $"{context}.Right");
                break;

            case NamedExpressionAst named:
                ValidateExpressionNames(named.Expression, $"{context}.{named.Name.Value}");
                break;

            case OptionalExpressionAst optional:
                ValidateExpressionNames(optional.Expression, $"{context}?");
                break;

            case OftenMissedExpressionAst oftenMissed:
                ValidateExpressionNames(oftenMissed.Expression, $"{context}??");
                break;

            case OneOrManyExpressionAst oneOrMany:
                ValidateExpressionNames(oneOrMany.Element, $"{context}+");
                break;

            case ZeroOrManyExpressionAst zeroOrMany:
                ValidateExpressionNames(zeroOrMany.Element, $"{context}*");
                break;

            case AndPredicateExpressionAst andPredicate:
                ValidateExpressionNames(andPredicate.Expression, $"&{context}");
                break;

            case NotPredicateExpressionAst notPredicate:
                ValidateExpressionNames(notPredicate.Expression, $"!{context}");
                break;

            case GroupExpressionAst group:
                ValidateExpressionNames(group.Expression, $"({context})");
                break;

            case SeparatedListExpressionAst separatedList:
                ValidateExpressionNames(separatedList.Element, $"{context}.Element");
                ValidateExpressionNames(separatedList.Separator, $"{context}.Separator");
                break;

            // Terminal RuleExpressionAst nodes that don't have nested expressions
            case RuleRefExpressionAst:
            case LiteralAst:
                break;

            default:
                throw new System.InvalidOperationException(
                    $"Unsupported expression type: {expression.GetType().Name}");
        }
    }
}
