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
    private void ValidateExpressionNames(RuleExpressionAst expression, string? context = null)
    {
        var subruleNames = Naming.CollectAllSubruleNames(expression, context);

        foreach (var kvp in subruleNames)
        {
            var subrule = kvp.Key;
            var name = kvp.Value;

            if (name is null && subrule is not OptionalExpressionAst)// && subrule is not OptionalExpressionAst)
            {
                // Special handling for SequenceExpressionAst
                if (subrule is SequenceExpressionAst)
                    _context.ReportError(
                        $"Sequence expression must have explicit names for all elements. Use 'Name=Expression' syntax for: {subrule}",
                        subrule);
                else
                    _context.ReportError(
                        $"Cannot infer name for subrule in {subrule}. Use explicit naming with 'Name=' syntax.",
                        subrule);
            }
        }

        foreach (var kvp in subruleNames)
        {
            validateSubruleNames(kvp.Key);
        }

        void validateSubruleNames(RuleExpressionAst expression)
        {
            switch (expression)
            {
                case SequenceExpressionAst seq:
                    foreach (var expr in seq.FlattenSequence())
                        ValidateExpressionNames(expr);
                    break;
                case NamedExpressionAst named:
                    validateSubruleNames(named.Expression);
                    break;
                case OptionalExpressionAst optional:
                    ValidateExpressionNames(optional.Expression);
                    break;
                case OftenMissedExpressionAst oftenMissed:
                    ValidateExpressionNames(oftenMissed.Expression);
                    break;
                case OneOrManyExpressionAst oneOrMany:
                    ValidateExpressionNames(oneOrMany.Element);
                    break;
                case ZeroOrManyExpressionAst zeroOrMany:
                    ValidateExpressionNames(zeroOrMany.Element);
                    break;
                case AndPredicateExpressionAst andPredicate:
                case NotPredicateExpressionAst notPredicate:
                    break;
                case GroupExpressionAst group:
                    ValidateExpressionNames(group.Expression);
                    break;
                case SeparatedListExpressionAst separatedList:
                    ValidateExpressionNames(separatedList.Element);
                    ValidateExpressionNames(separatedList.Separator);
                    break;
                // Terminal RuleExpressionAst nodes that don't have nested expressions
                case RuleRefExpressionAst:
                case LiteralAst:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported expression type: {expression.GetType().Name}");
            }
        }

        //// Also recursively validate nested expressions
        //switch (expression)
        //{
        //    case SequenceExpressionAst seq:
        //        foreach (var expr in seq.FlattenSequence())
        //            ValidateExpressionNames(expr, context: null);
        //        break;
        //    case NamedExpressionAst named:
        //        ValidateExpressionNames(named.Expression, named.Name.Value);
        //        break;
        //    case OptionalExpressionAst optional:
        //        ValidateExpressionNames(optional.Expression, context: null);
        //        break;
        //    case OftenMissedExpressionAst oftenMissed:
        //        ValidateExpressionNames(oftenMissed.Expression, context: null);
        //        break;
        //    case OneOrManyExpressionAst oneOrMany:
        //        ValidateExpressionNames(oneOrMany.Element, context: null);
        //        break;
        //    case ZeroOrManyExpressionAst zeroOrMany:
        //        ValidateExpressionNames(zeroOrMany.Element, context: null);
        //        break;
        //    case AndPredicateExpressionAst andPredicate:
        //        ValidateExpressionNames(andPredicate.Expression, context: null);
        //        break;
        //    case NotPredicateExpressionAst notPredicate:
        //        ValidateExpressionNames(notPredicate.Expression, context: null);
        //        break;
        //    case GroupExpressionAst group:
        //        ValidateExpressionNames(group.Expression, context: null);
        //        break;
        //    case SeparatedListExpressionAst separatedList:
        //        ValidateExpressionNames(separatedList.Element, context: null);
        //        ValidateExpressionNames(separatedList.Separator, context: null);
        //        break;
        //    // Terminal RuleExpressionAst nodes that don't have nested expressions
        //    case RuleRefExpressionAst:
        //    case LiteralAst:
        //        break;
        //    default:
        //        throw new System.InvalidOperationException(
        //            $"Unsupported expression type: {expression.GetType().Name}");
        //}
    }
}
