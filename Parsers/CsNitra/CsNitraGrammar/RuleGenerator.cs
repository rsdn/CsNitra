using System.Collections.Generic;
using System.Linq;
using ExtensibleParaser;
using EP = ExtensibleParaser; // Псевдоним для ExtensibleParaser

namespace CsNitra;

/// <summary>
/// Генератор правил для парсера
/// </summary>
public sealed class RuleGenerator(Scope globalScope, Parser parser)
{
    public void GenerateRules()
    {
        foreach (var ruleSymbol in globalScope.GetAllRules())
        {
            if (ruleSymbol.RuleStatement != null)
            {
                parser.Rules[ruleSymbol.Name.Value] = GenerateRuleFromStatement(ruleSymbol.RuleStatement);
            }
            else if (ruleSymbol.SimpleRuleStatement != null)
            {
                var rule = GenerateSimpleRule(ruleSymbol.SimpleRuleStatement);
                parser.Rules[ruleSymbol.Name.Value] = new[] { rule };
            }
        }
    }

    private Rule[] GenerateRuleFromStatement(RuleStatementAst node)
    {
        var alternatives = new List<Rule>();

        foreach (var alternative in node.Alternatives)
        {
            var rule = alternative switch
            {
                NamedAlternativeAst named => GenerateExpression(named.Expression, node.Name.Value),
                AnonymousAlternativeAst anon => GenerateAnonymousAlternative(anon, node.Name.Value),
                _ => throw new InvalidOperationException($"Unknown alternative type: {alternative.GetType()}")
            };

            alternatives.Add(rule);
        }

        return alternatives.ToArray();
    }

    private Rule GenerateSimpleRule(SimpleRuleStatementAst node) =>
        GenerateExpression(node.Expression, node.Name.Value);

    private Rule GenerateAnonymousAlternative(AnonymousAlternativeAst node, string context)
    {
        if (node.RuleRef.Parts.Count == 1)
        {
            var identifier = node.RuleRef.Parts[0];
            return GenerateSimpleReference(identifier, context);
        }

        return new Ref(node.RuleRef.ToString());
    }

    private Rule GenerateExpression(RuleExpressionAst expression, string context)
    {
        return expression switch
        {
            RuleRefExpressionAst ruleRef => GenerateRuleRefExpression(ruleRef, context),
            SequenceExpressionAst seq => GenerateSequenceExpression(seq, context),
            NamedExpressionAst named => GenerateNamedExpression(named, context),
            OptionalExpressionAst opt => new Optional(GenerateExpression(opt.Expression, context)),
            OftenMissedExpressionAst om => new OftenMissed(GenerateExpression(om.Expression, context)),
            OneOrManyExpressionAst oneOrMany => new OneOrMany(GenerateExpression(oneOrMany.Expression, context)),
            ZeroOrManyExpressionAst zeroOrMany => new ZeroOrMany(GenerateExpression(zeroOrMany.Expression, context)),
            AndPredicateExpressionAst and => GenerateAndPredicate(and, context),
            NotPredicateExpressionAst not => GenerateNotPredicate(not, context),
            StringLiteralAst str => new EP.Literal(str.Value),
            CharLiteralAst ch => new EP.Literal(ch.Value),
            GroupExpressionAst group => GenerateExpression(group.Expression, context),
            SeparatedListExpressionAst list => GenerateSeparatedList(list, context),
            _ => throw new InvalidOperationException($"Unknown expression type: {expression.GetType()}")
        };
    }

    private Rule GenerateRuleRefExpression(RuleRefExpressionAst node, string context)
    {
        if (node.Ref.Parts.Count == 1)
        {
            var identifier = node.Ref.Parts[0];

            if (node.Precedence != null && node.PrecedenceSymbol != null)
            {
                var bindingPower = node.PrecedenceSymbol.BindingPower;
                var rightAssoc = node.PrecedenceSymbol.IsRightAssociative;

                return new ReqRef(identifier.Value, bindingPower, rightAssoc);
            }

            return GenerateSimpleReference(identifier, context);
        }

        return new Ref(node.Ref.ToString());
    }

    private Rule GenerateSimpleReference(Identifier identifier, string context)
    {
        return new Ref(identifier.Value);
    }

    private Rule GenerateSequenceExpression(SequenceExpressionAst node, string context)
    {
        var left = GenerateExpression(node.Left, context);
        var right = GenerateExpression(node.Right, context);

        if (left is Seq leftSeq && right is Seq rightSeq)
            return new Seq(leftSeq.Elements.Concat(rightSeq.Elements).ToArray(), context);

        if (left is Seq leftSeq2)
            return new Seq(leftSeq2.Elements.Append(right).ToArray(), context);

        if (right is Seq rightSeq2)
            return new Seq(new[] { left }.Concat(rightSeq2.Elements).ToArray(), context);

        return new Seq(new[] { left, right }, context);
    }

    private Rule GenerateNamedExpression(NamedExpressionAst node, string context)
    {
        var inner = GenerateExpression(node.Expression, context);

        return inner switch
        {
            EP.Literal lit => lit with { Kind = $"{context}.{node.Name}" },
            ReqRef rrf => rrf with { Kind = $"{context}.{node.Name}" },
            Ref rf => rf with { Kind = $"{context}.{node.Name}" },
            Seq seq => seq with { Kind = $"{context}.{node.Name}" },
            Optional opt => opt with { Kind = $"{context}.{node.Name}" },
            OftenMissed om => om with { Kind = $"{context}.{node.Name}" },
            OneOrMany oom => oom with { Kind = $"{context}.{node.Name}" },
            ZeroOrMany zom => zom with { Kind = $"{context}.{node.Name}" },
            AndPredicate and => and with { Kind = $"{context}.{node.Name}" },
            NotPredicate not => not with { Kind = $"{context}.{node.Name}" },
            SeparatedList sl => sl with { Kind = $"{context}.{node.Name}" },
            var innerRule => innerRule
        };
    }

    private Rule GenerateAndPredicate(AndPredicateExpressionAst node, string context)
    {
        var predicate = GenerateExpression(node.Expression, context);
        return new AndPredicate(predicate);
    }

    private Rule GenerateNotPredicate(NotPredicateExpressionAst node, string context)
    {
        var predicate = GenerateExpression(node.Expression, context);
        return new NotPredicate(predicate);
    }

    private Rule GenerateSeparatedList(SeparatedListExpressionAst node, string context)
    {
        var element = GenerateExpression(node.Element, context);
        var separator = GenerateExpression(node.Separator, context);

        var endBehavior = node.Modifier?.Value switch
        {
            "?" => SeparatorEndBehavior.Optional,
            "!" => SeparatorEndBehavior.Required,
            _ => SeparatorEndBehavior.Optional
        };

        var canBeEmpty = node.Count == "*";

        return new SeparatedList(element, separator, context, endBehavior, canBeEmpty);
    }
}
