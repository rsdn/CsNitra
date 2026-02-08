using ExtensibleParser;
using EP = ExtensibleParser;

namespace CsNitra;

using Ast;
using CsNitra.TypeChecking;

public sealed class RuleGenerator(Scope globalScope, Parser parser)
{
    public void GenerateRules()
    {
        foreach (var ruleSymbol in globalScope.GetAllRules())
            if (ruleSymbol.RuleStatement != null)
                parser.Rules[ruleSymbol.Name.Value] = GenerateRuleFromStatement(ruleSymbol.RuleStatement);
            else if (ruleSymbol.SimpleRuleStatement != null)
                parser.Rules[ruleSymbol.Name.Value] = new[] { GenerateSimpleRule(ruleSymbol.SimpleRuleStatement) };
    }

    private Rule[] GenerateRuleFromStatement(RuleStatementAst node)
    {
        var alternatives = new List<Rule>();

        foreach (var alternative in node.Alternatives)
        {
            var rule = alternative switch
            {
                NamedAlternativeAst named => GenerateExpression(named.Expression),
                AnonymousAlternativeAst anon => GenerateAnonymousAlternative(anon),
                _ => throw new InvalidOperationException($"Unknown alternative type: {alternative.GetType()}")
            };

            alternatives.Add(rule);
        }

        return alternatives.ToArray();
    }

    private Rule GenerateSimpleRule(SimpleRuleStatementAst node) =>
        GenerateExpression(node.Expression);

    private Rule GenerateAnonymousAlternative(AnonymousAlternativeAst node)
    {
        var ruleName = node.RuleRef.ToString();
        return globalScope.FindTerminal(ruleName) is { } terminal ? terminal.Terminal : (Rule)new Ref(ruleName);
    }

    private Rule GenerateExpression(RuleExpressionAst expression)
    {
        return expression switch
        {
            RuleRefExpressionAst a => GenerateRuleRefExpression(a),
            FlattenSequenceExpressionAst a => GenerateSequenceExpression(a),
            OptionalExpressionAst a => new Optional(GenerateExpression(a.Expression), a.Kind),
            OftenMissedExpressionAst a => new OftenMissed(GenerateExpression(a.Expression), a.Kind ?? "Error"),
            OneOrManyExpressionAst a => new OneOrMany(GenerateExpression(a.Element), a.Kind),
            ZeroOrManyExpressionAst a => new ZeroOrMany(GenerateExpression(a.Element), a.Kind),
            AndPredicateExpressionAst a => GenerateAndPredicate(a),
            NotPredicateExpressionAst a => GenerateNotPredicate(a),
            LiteralAst a => new EP.Literal(a.Value, a.Kind),
            GroupExpressionAst a => GenerateExpression(a.Expression),
            SeparatedListExpressionAst a => GenerateSeparatedList(a),
            _ => throw new InvalidOperationException($"Unknown expression type: {expression.GetType()}")
        };
    }

    private Rule GenerateRuleRefExpression(RuleRefExpressionAst node)
    {
        var refName = node.Ref.ToString();

        return node.Precedence != null
            ? new ReqRef(
                refName,
                Precedence: node.PrecedenceSymbol.AssertIsNonNull().BindingPower,
                Right: node.Precedence.Associativity != null,
                node.Kind)
            : globalScope.FindTerminal(refName) is { } terminal
                ? terminal.Terminal
                : (Rule)new Ref(refName, node.Kind);
    }

    private Rule GenerateSequenceExpression(FlattenSequenceExpressionAst seq) =>
        new Seq(seq.Elements.Select(GenerateExpression).ToArray(), seq.Kind.AssertIsNonNull());

    private Rule GenerateAndPredicate(AndPredicateExpressionAst node) =>
        new AndPredicate(GenerateExpression(node.Expression));

    private Rule GenerateNotPredicate(NotPredicateExpressionAst node) =>
        new NotPredicate(GenerateExpression(node.Expression));

    private Rule GenerateSeparatedList(SeparatedListExpressionAst node)
    {
        var element = GenerateExpression(node.Element);
        var separator = GenerateExpression(node.Separator);
        var endBehavior = node.Modifier?.Value switch
        {
            "?" => SeparatorEndBehavior.Optional,
            "!" => SeparatorEndBehavior.Required,
            _ => SeparatorEndBehavior.Forbidden
        };
        var canBeEmpty = node.Count.Value == "*";
        return new SeparatedList(element, separator, node.Kind.AssertIsNonNull(), endBehavior, canBeEmpty);
    }
}
