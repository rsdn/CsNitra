using ExtensibleParaser;
using System.Drawing;
using System.IO;
using System.Xml.Linq;

namespace CsNitra;

public class CsNitraVisitor(string input) : ISyntaxVisitor
{
    private CsNitraAst? _currentResult;

    public CsNitraAst Result { get; private set; } = Option.None;

    public void Visit(TerminalNode node)
    {
        var value = node.ToString(input);

        _currentResult = node.Kind switch
        {
            "Identifier" => new Identifier(value, node.StartPos, node.EndPos),
            "StringLiteral" => new StringLiteralAst(UnescapeString(value), node.StartPos, node.EndPos),
            "CharLiteral" => new CharLiteralAst(UnescapeChar(value), node.StartPos, node.EndPos),
            "left" or "right" => new Literal(value, node.StartPos, node.EndPos),
            "?" or "!" => new Literal(value, node.StartPos, node.EndPos),
            "+" or "*" => new Literal(value, node.StartPos, node.EndPos),
            _ => new Literal(value, node.StartPos, node.EndPos)
        };
    }

    private static string UnescapeString(string value)
    {
        if (value.Length >= 2)
            return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        return value;
    }

    private static string UnescapeChar(string value)
    {
        if (value.Length >= 2)
            return value[1..^1].Replace("\\'", "'").Replace("\\\\", "\\");
        return value;
    }

    public void Visit(SeqNode node)
    {
        var children = new List<CsNitraAst?>();

        foreach (var element in node.Elements)
        {
            element.Accept(this);
            children.Add(_currentResult);
            _currentResult = null;
        }

        var startPos = node.StartPos;
        var endPos = node.EndPos;

        try
        {
            _currentResult = node.Kind switch
            {
                "Grammar" => ProcessGrammar(children, startPos, endPos),
                "OpenUsing" => ProcessOpenUsing(children, startPos, endPos),
                "AliasUsing" => ProcessAliasUsing(children, startPos, endPos),
                "Precedence" => ProcessPrecedenceStatement(children, startPos, endPos),
                "Rule" => ProcessRuleStatement(children, startPos, endPos),
                "SequenceExpression" => ProcessSequenceExpression(children, startPos, endPos),
                "Named" => ProcessNamedExpression(children, startPos, endPos),
                "Optional" => ProcessOptionalExpression(children, startPos, endPos),
                "OftenMissed" => ProcessOftenMissedExpression(children, startPos, endPos),
                "OneOrMany" => ProcessOneOrManyExpression(children, startPos, endPos),
                "ZeroOrMany" => ProcessZeroOrManyExpression(children, startPos, endPos),
                "AndPredicateExpression" => ProcessAndPredicateExpression(children, startPos, endPos),
                "NotPredicateExpression" => ProcessNotPredicateExpression(children, startPos, endPos),
                "RuleRef" => ProcessRuleRefExpression(children, startPos, endPos),
                "PrecedenceWithAssociativity" => ProcessPrecedenceWithAssociativity(children, startPos, endPos),
                "Group" => ProcessGroupExpression(children, startPos, endPos),
                "SeparatedListExpression" => ProcessSeparatedListExpression(children, startPos, endPos),
                "Usings" => ProcessAstList<UsingAst>(children, startPos, endPos),
                "Statements" => ProcessAstList<StatementAst>(children, startPos, endPos),
                _ => throw new InvalidOperationException($"Unknown SeqNode kind: {node.Kind}")
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error processing {node.Kind}: {ex.Message}", ex);
        }

        if (node.Kind == "Grammar" && _currentResult is GrammarAst grammar)
            Result = grammar;
    }

    private CsNitraAst ProcessPrecedenceStatement(List<CsNitraAst?> children, int startPos, int endPos) => children switch
    {
        [Literal precedenceKw, AstList<Identifier> list, Literal semicolon] =>
            new PrecedenceStatementAst(precedenceKw, list.Items, semicolon, startPos, endPos),
        _ => throw new InvalidOperationException("Expected precedence statement"),
    };

    private CsNitraAst ProcessGrammar(List<CsNitraAst?> children, int startPos, int endPos) => children switch
    {
        [AstList<UsingAst> usings, AstList<StatementAst> statements] => new GrammarAst(usings.Items, statements.Items, startPos, endPos),
        _ => throw new InvalidOperationException("Expected Grammar"),
    };

    private CsNitraAst ProcessOpenUsing(List<CsNitraAst?> children, int startPos, int endPos) => children switch
    {
        [Literal usingKw, AstList<Identifier> qi, Literal semicolon] => new OpenUsingAst(usingKw, qi.Items, semicolon, startPos, endPos),
        _ => throw new InvalidOperationException("Expected QualifiedIdentifier in OpenUsing")
    };

    private CsNitraAst ProcessAliasUsing(List<CsNitraAst?> children, int startPos, int endPos)
    {
        var alias = children[1] as Identifier
            ?? throw new InvalidOperationException("Expected alias identifier");

        var qualifiedId = children[3] as QualifiedIdentifierAst
            ?? throw new InvalidOperationException("Expected QualifiedIdentifier in AliasUsing");

        return new AliasUsingAst(alias.Value, qualifiedId, startPos, endPos);
    }

    private CsNitraAst ProcessRuleStatement(List<CsNitraAst?> children, int startPos, int endPos) => children switch
    {
        [
            Identifier ruleName,
            Literal equals,
            Option pipeOpt,
            AstList<CsNitraAst> alternativesList,
            Literal semicolon
        ] =>
            new RuleStatementAst(
                Name: ruleName,
                Eq: equals,
                Alternatives: ProcessRuleAlternatives(ruleName, alternativesList.Items, startPos, endPos),
                StartPos: startPos,
                EndPos: endPos
            ),
        _ => throw new InvalidOperationException($"Invalid rule statement: {string.Join(", ", children)}")
    };

    private IReadOnlyList<RuleAlternativeAst> ProcessRuleAlternatives(
        Identifier ruleName,
        List<CsNitraAst> expressionItems,
        int ruleStartPos,
        int ruleEndPos)
    {
        var alternatives = new List<RuleAlternativeAst>();
        var currentAlternativeItems = new List<RuleExpressionAst>();
        var currentName = ruleName; // Начинаем с имени правила
        var alternativeStartPos = ruleStartPos;

        foreach (var item in expressionItems)
        {
            switch (item)
            {
                case Literal("|", var pipeStart, var pipeEnd) pipe:
                    // Завершаем текущую альтернативу
                    if (currentAlternativeItems.Count > 0)
                    {
                        var alternativeEndPos = pipeStart;
                        alternatives.Add(new RuleAlternativeAst(
                            Name: currentName,
                            SubRules: currentAlternativeItems,
                            StartPos: alternativeStartPos,
                            EndPos: alternativeEndPos
                        ));
                    }

                    // Начинаем новую альтернативу
                    currentAlternativeItems = new List<RuleExpressionAst>();
                    currentName = new Identifier("anonymous", pipeEnd, pipeEnd);
                    alternativeStartPos = pipeEnd;
                    break;

                case RuleExpressionAst expr:
                    currentAlternativeItems.Add(expr);

                    // Если это NamedExpression, используем его имя для альтернативы
                    if (expr is NamedExpressionAst(_, var namedExpr, _, _) named)
                        currentName = new Identifier(named.Name, expr.StartPos, expr.EndPos);
                    break;
            }
        }

        // Добавляем последнюю альтернативу
        if (currentAlternativeItems.Count > 0)
            alternatives.Add(new RuleAlternativeAst(
                Name: currentName,
                SubRules: currentAlternativeItems,
                StartPos: alternativeStartPos,
                EndPos: ruleEndPos
            ));

        return alternatives;
    }

    Literal? GetOptonLiteral(Option option) => option switch { Some<CsNitraAst>(Literal result) => result, _ => null };

    private IReadOnlyList<RuleExpressionAst> ToList(RuleExpressionAst expr, List<RuleExpressionAst>? result = null)
    {
        result ??= new List<RuleExpressionAst>();

        if (expr is SequenceExpressionAst(var left, var right, _, _))
        {
            result.Add(left);
            return ToList(right, result);
        }

        result.Add(expr);
        return result;
    }
  
    private CsNitraAst ProcessSequenceExpression(List<CsNitraAst?> children, int startPos, int endPos)
    {
        var left = children[0] as RuleExpressionAst
            ?? throw new InvalidOperationException("Expected left expression in Sequence");

        var right = children[1] as RuleExpressionAst
            ?? throw new InvalidOperationException("Expected right expression in Sequence");

        return new SequenceExpressionAst(left, right, startPos, endPos);
    }

    private CsNitraAst ProcessNamedExpression(List<CsNitraAst?> children, int startPos, int endPos)
    {
        var name = children[0] as Identifier
            ?? throw new InvalidOperationException("Expected name in Named expression");

        var expression = children[2] as RuleExpressionAst
            ?? throw new InvalidOperationException("Expected expression in Named expression");

        return new NamedExpressionAst(name.Value, expression, startPos, endPos);
    }

    private CsNitraAst ProcessOptionalExpression(List<CsNitraAst?> children, int startPos, int endPos)
    {
        var expression = children[0] as RuleExpressionAst
            ?? throw new InvalidOperationException("Expected expression in Optional");

        return new OptionalExpressionAst(expression, startPos, endPos);
    }

    private CsNitraAst ProcessOftenMissedExpression(List<CsNitraAst?> children, int startPos, int endPos)
    {
        var expression = children[0] as RuleExpressionAst
            ?? throw new InvalidOperationException("Expected expression in OftenMissed");

        return new OftenMissedExpressionAst(expression, startPos, endPos);
    }

    private CsNitraAst ProcessOneOrManyExpression(List<CsNitraAst?> children, int startPos, int endPos) => children switch
    {
        [RuleExpressionAst element, Literal plus] => new OneOrManyExpressionAst(element, plus, startPos, endPos),
        _ => throw new InvalidOperationException("Expected expression in ZeroOrMany"),
    };

    private CsNitraAst ProcessZeroOrManyExpression(List<CsNitraAst?> children, int startPos, int endPos) => children switch
    {
        [RuleExpressionAst element, Literal star] => new ZeroOrManyExpressionAst(element, star, startPos, endPos),
        _ => throw new InvalidOperationException("Expected expression in ZeroOrMany"),
    };

    private CsNitraAst ProcessAndPredicateExpression(List<CsNitraAst?> children, int startPos, int endPos)
    {
        var expression = children[1] as RuleExpressionAst
            ?? throw new InvalidOperationException("Expected expression in AndPredicate");

        return new AndPredicateExpressionAst(expression, startPos, endPos);
    }

    private CsNitraAst ProcessNotPredicateExpression(List<CsNitraAst?> children, int startPos, int endPos)
    {
        var expression = children[1] as RuleExpressionAst
            ?? throw new InvalidOperationException("Expected expression in NotPredicate");

        return new NotPredicateExpressionAst(expression, startPos, endPos);
    }

    private CsNitraAst ProcessRuleRefExpression(List<CsNitraAst?> children, int startPos, int endPos) => children switch
    {
        [AstList<Identifier> parts, None] => new RuleRefExpressionAst(new QualifiedIdentifierAst(parts.Items, parts.Items.First().StartPos, parts.Items.First().EndPos), Precedence: null, Associativity: null, startPos, endPos),
        [AstList<Identifier> parts, Some<CsNitraAst> precedence] => new RuleRefExpressionAst(new QualifiedIdentifierAst(parts.Items, parts.Items.First().StartPos, parts.Items.First().EndPos), Precedence: null, Associativity: null, startPos, endPos),
        _ => throw new InvalidOperationException($"Expected RuleRef expression. But fond [{string.Join(", ", children.Select(x => x!.ToString()))}]")
    };

    private CsNitraAst ProcessPrecedenceWithAssociativity(List<CsNitraAst?> children, int startPos, int endPos)
    {
        var precedence = children[1] as Identifier
            ?? throw new InvalidOperationException("Expected precedence identifier");

        string? associativity = null;

        if (children[2] is StringValue assoc)
            associativity = assoc.Value;

        return new PrecedenceInfo(precedence.Value, associativity, startPos, endPos);
    }

    private CsNitraAst ProcessGroupExpression(List<CsNitraAst?> children, int startPos, int endPos)
    {
        var expression = children[1] as RuleExpressionAst
            ?? throw new InvalidOperationException("Expected expression in Group");

        return new GroupExpressionAst(expression, startPos, endPos);
    }

    private CsNitraAst ProcessSeparatedListExpression(List<CsNitraAst?> children, int startPos, int endPos) => children switch
    {
        [Literal opening, RuleExpressionAst element, Literal semicolon, RuleExpressionAst separator, None, Literal closing, Literal count] =>
            new SeparatedListExpressionAst(element, separator, Modifier: null, count.Value, startPos, endPos),
        [Literal opening, RuleExpressionAst element, Literal semicolon, RuleExpressionAst separator, Some<CsNitraAst>(Literal modifier), Literal closing, Literal count] =>
            new SeparatedListExpressionAst(element, separator, modifier, count.Value, startPos, endPos),
        _ => throw new InvalidOperationException($"Expected SeparatedList expression. But fond [{string.Join(", ", children.Select(x => x!.ToString()))}]")
    };

    private CsNitraAst ProcessAstList<T>(ListNode node) where T : CsNitraAst
    {
        var items = new List<T>();

        foreach (var element in node.Elements)
        {
            element.Accept(this);
            if (_currentResult is T result)
                items.Add(result);
            _currentResult = null;
        }

        return new AstList<T>(items, node.StartPos, node.EndPos);
    }

    private CsNitraAst ProcessAstDelimitedList<TElement, TDelimiter>(ListNode node)
        where TElement : CsNitraAst
        where TDelimiter : CsNitraAst
    {
        var elements = new List<TElement>();
        var delimiters = new List<TDelimiter>();

        foreach (var element in node.Elements)
        {
            element.Accept(this);
            if (_currentResult is TElement result)
                elements.Add(result);
            _currentResult = null;
        }

        foreach (var delimiter in node.Delimiters)
        {
            delimiter.Accept(this);
            if (_currentResult is TDelimiter result)
                delimiters.Add(result);
            _currentResult = null;
        }

        return new AstDelimitedList<TElement, TDelimiter>(elements, delimiters, node.StartPos, node.EndPos);
    }

    private CsNitraAst ProcessAstList<T>(List<CsNitraAst?> children, int startPos, int endPos) where T : CsNitraAst
    {
        var items = new List<T>();
        foreach (var child in children)
            if (child is T item)
                items.Add(item);

        return new AstList<T>(items, startPos, endPos);
    }

    public void Visit(ListNode node) => _currentResult = node.Kind switch
    {
        "QualifiedIdentifier" or "Precedences" => ProcessAstList<Identifier>(node),
        _ => ProcessAstList<CsNitraAst>(node),
    };

    public void Visit(SomeNode node)
    {
        node.Value.Accept(this);
        _currentResult = Option.Some(_currentResult.AssertIsNonNull());
    }

    public void Visit(NoneNode node) => _currentResult = Option.None;

    // Helper classes for visitor
    private record AstList<T>(List<T> Items, int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos) where T : CsNitraAst;
    private record AstDelimitedList<TElement, TDelimiter>(List<TElement> Items, List<TDelimiter> Delimiters, int StartPos, int EndPos) : AstList<TElement>(Items, StartPos, EndPos)
        where TElement : CsNitraAst
        where TDelimiter : CsNitraAst;
    private record StringValue(string Value, int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);
}
