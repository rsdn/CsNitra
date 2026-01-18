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

    private CsNitraAst ProcessGrammar(List<CsNitraAst?> children, int startPos, int endPos)
    {
        var usings = new List<UsingAst>();
        var statements = new List<StatementAst>();

        foreach (var child in children)
        {
            //if (child is AstList list)
            //{
            //    foreach (var item in list.Items)
            //    {
            //        if (item is UsingAst usingAst) usings.Add(usingAst);
            //        else if (item is StatementAst statementAst) statements.Add(statementAst);
            //    }
            //}
        }

        return new GrammarAst(usings, statements, startPos, endPos);
    }

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
        [Identifier name, Literal equals, Option pipe, AstList<CsNitraAst>([RuleExpressionAst expr], _, _), Literal semicolon] =>
            new RuleStatementAst(name, equals, ToList(expr), startPos, endPos),
        _ => throw new InvalidOperationException("Expected Rule statement"),
    };

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
    private record StringValue(string Value, int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);
}
