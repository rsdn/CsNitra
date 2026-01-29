using ExtensibleParaser;
using EP = ExtensibleParaser; // Псевдоним для ExtensibleParaser

namespace CsNitra;

using Ast;

/// <summary>
/// Генератор правил для парсера
/// </summary>
public sealed class RuleGenerator(Scope globalScope, Parser parser)
{
    public void GenerateRules()
    {
        foreach (var ruleSymbol in globalScope.GetAllRules())
        {
            if (ruleSymbol.Name.Value is "StringLiteral" or "Identifier")
            {
            }

            if (ruleSymbol.RuleStatement != null)
                parser.Rules[ruleSymbol.Name.Value] = GenerateRuleFromStatement(ruleSymbol.RuleStatement);
            else if (ruleSymbol.SimpleRuleStatement != null)
                parser.Rules[ruleSymbol.Name.Value] = new[] { GenerateSimpleRule(ruleSymbol.SimpleRuleStatement) };
        }
    }

    private Rule[] GenerateRuleFromStatement(RuleStatementAst node)
    {
        var alternatives = new List<Rule>();

        foreach (var alternative in node.Alternatives)
        {
            var rule = alternative switch
            {
                NamedAlternativeAst named => GenerateExpression(named.Expression, named.Name.Value),
                AnonymousAlternativeAst anon => GenerateAnonymousAlternative(anon),
                _ => throw new InvalidOperationException($"Unknown alternative type: {alternative.GetType()}")
            };

            alternatives.Add(rule);
        }

        return alternatives.ToArray();
    }

    private Rule GenerateSimpleRule(SimpleRuleStatementAst node) =>
        GenerateExpression(node.Expression, node.Name.Value);

    private Rule GenerateAnonymousAlternative(AnonymousAlternativeAst node)
    {
        var ruleName = node.RuleRef.ToString();

        if (globalScope.FindTerminal(ruleName) is { } terminal)
            return terminal.Terminal;

        return new Ref(ruleName);
    }

    private Rule GenerateExpression(RuleExpressionAst expression, string? name)
    {
        return expression switch
        {
            RuleRefExpressionAst ruleRef => GenerateRuleRefExpression(ruleRef, name),
            SequenceExpressionAst seq => GenerateSequenceExpression(seq, name),
            NamedExpressionAst named => GenerateNamedExpression(named),
            OptionalExpressionAst opt => new Optional(GenerateExpression(opt.Expression, name: null), name),
            OftenMissedExpressionAst om => new OftenMissed(GenerateExpression(om.Expression, name: null), name ?? "Error"),
            OneOrManyExpressionAst oneOrMany => new OneOrMany(GenerateExpression(oneOrMany.Expression, name: null), name),
            ZeroOrManyExpressionAst zeroOrMany => new ZeroOrMany(GenerateExpression(zeroOrMany.Expression, name: null), name),
            AndPredicateExpressionAst and => GenerateAndPredicate(and),
            NotPredicateExpressionAst not => GenerateNotPredicate(not),
            StringLiteralAst str => new EP.Literal(str.Value, name),
            CharLiteralAst ch => new EP.Literal(ch.Value, name),
            GroupExpressionAst group => GenerateExpression(group.Expression, name),
            SeparatedListExpressionAst list => GenerateSeparatedList(list, name),
            _ => throw new InvalidOperationException($"Unknown expression type: {expression.GetType()}")
        };
    }

    private Rule GenerateRuleRefExpression(RuleRefExpressionAst node, string? name)
    {
        var refName = node.Ref.ToString();

        if (node.Precedence != null && node.PrecedenceSymbol != null)
        {
            var bindingPower = node.PrecedenceSymbol.BindingPower;
            var rightAssoc = node.PrecedenceSymbol.IsRightAssociative;

            return new ReqRef(refName, bindingPower, rightAssoc);
        }

        if (globalScope.FindTerminal(refName) is { } terminal)
            return terminal.Terminal;

        return new Ref(refName, Kind: name);
    }

    private Rule GenerateSequenceExpression(SequenceExpressionAst node, string? name)
    {
        var left = GenerateExpression(node.Left, name: node.Left is SequenceExpressionAst ? name : null);
        var right = GenerateExpression(node.Right, name: node.Right is SequenceExpressionAst ? name : null);

        if (left is Seq leftSeq && right is Seq rightSeq)
            return new Seq(leftSeq.Elements.Concat(rightSeq.Elements).ToArray(), name.AssertIsNonNull());

        if (left is Seq leftSeq2)
            return new Seq(leftSeq2.Elements.Append(right).ToArray(), name.AssertIsNonNull());

        if (right is Seq rightSeq2)
            return new Seq(new[] { left }.Concat(rightSeq2.Elements).ToArray(), name.AssertIsNonNull());

        return new Seq([left, right], name.AssertIsNonNull());
    }

    private Rule GenerateNamedExpression(NamedExpressionAst node) => GenerateExpression(node.Expression, node.Name);

    private Rule GenerateAndPredicate(AndPredicateExpressionAst node)
    {
        var predicate = GenerateExpression(node.Expression, name: null);
        return new AndPredicate(predicate);
    }

    private Rule GenerateNotPredicate(NotPredicateExpressionAst node)
    {
        var predicate = GenerateExpression(node.Expression, name: null);
        return new NotPredicate(predicate);
    }

    private Rule GenerateSeparatedList(SeparatedListExpressionAst node, string? name)
    {
        var element = GenerateExpression(node.Element, name: null);
        var separator = GenerateExpression(node.Separator, name: null);

        var endBehavior = node.Modifier?.Value switch
        {
            "?" => SeparatorEndBehavior.Optional,
            "!" => SeparatorEndBehavior.Required,
            _ => SeparatorEndBehavior.Forbidden
        };

        var canBeEmpty = node.Count == "*";

        return new SeparatedList(element, separator, Kind: name.AssertIsNonNull(), endBehavior, canBeEmpty);
    }
}
