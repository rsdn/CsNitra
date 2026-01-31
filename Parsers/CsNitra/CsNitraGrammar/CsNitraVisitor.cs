using ExtensibleParaser;

namespace CsNitra;

using Ast;

public class CsNitraVisitor(string input) : ISyntaxVisitor
{
    private CsNitraAst? _currentResult;

    public CsNitraAst Result { get; private set; } = Option.None;

    public void Visit(TerminalNode node)
    {
        var value = node.ToString(input);
        var endPos = node.StartPos + node.ContentLength;
        _currentResult = node.Kind switch
        {
            "Identifier" => new Identifier(value, node.StartPos, endPos),
            "Literal" => new LiteralAst(UnescapeString(value), node.StartPos, endPos),
            "left" or "right" => new Literal(value, node.StartPos, endPos),
            "?" or "!" => new Literal(value, node.StartPos, endPos),
            "+" or "*" => new Literal(value, node.StartPos, endPos),
            _ => new Literal(value, node.StartPos, endPos)
        };
    }

    private static string UnescapeString(string value)
    {
        if (value.Length >= 2)
            return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        return value;
    }

    public void Visit(SeqNode node)
    {
        var children = new List<CsNitraAst>();

        foreach (var element in node.Elements)
        {
            element.Accept(this);
            children.Add(_currentResult.AssertIsNonNull());
            _currentResult = null;
        }

        var startPos = node.StartPos;
        var endPos = node.EndPos;

        try
        {
            _currentResult = node.Kind switch
            {
                "QualifiedIdentifier" => throw new InvalidOperationException($"Unknown SeqNode kind: {node.Kind}"),
                "Grammar" => ProcessGrammar(children, startPos, endPos),
                "OpenUsing" => ProcessOpenUsing(children, startPos, endPos),
                "AliasUsing" => ProcessAliasUsing(children, startPos, endPos),
                "Precedence" => ProcessPrecedenceStatement(children, startPos, endPos),
                "Associativity" => ProcessAssociativity(children, startPos, endPos),
                "Rule" => ProcessRuleStatement(children, startPos, endPos),
                "SimpleRule" => ProcessSimpleRuleStatement(children, startPos, endPos),
                "NamedAlternative" => ProcessNamedAlternative(children, startPos, endPos),
                "AnonymousAlternative" => ProcessAnonymousAlternative(children, startPos, endPos),
                "Sequence" => ProcessSequenceExpression(children, startPos, endPos),
                "Named" => ProcessNamedExpression(children, startPos, endPos),
                "Optional" => ProcessOptionalExpression(children, startPos, endPos),
                "OftenMissed" => ProcessOftenMissedExpression(children, startPos, endPos),
                "OneOrMany" => ProcessOneOrManyExpression(children, startPos, endPos),
                "ZeroOrMany" => ProcessZeroOrManyExpression(children, startPos, endPos),
                "AndPredicate" => ProcessAndPredicateExpression(children, startPos, endPos),
                "NotPredicate" => ProcessNotPredicateExpression(children, startPos, endPos),
                "RuleRef" => ProcessRuleRefExpression(children, startPos, endPos),
                "PrecedenceWithAssociativity" => ProcessPrecedenceWithAssociativity(children, startPos, endPos),
                "Group" => ProcessGroupExpression(children, startPos, endPos),
                "SeparatedList" => ProcessSeparatedListExpression(children, startPos, endPos),
                "Usings" => ProcessAstList<UsingAst>(children, startPos, endPos),
                "Statements" => ProcessAstList<StatementAst>(children, startPos, endPos),
                "Alternatives" => ProcessAstList<AlternativeAst>(children, startPos, endPos),
                "Elem" => NamedAlternative(children, startPos, endPos),
                _ => throw new InvalidOperationException($"Unknown SeqNode kind: {node.Kind}"),
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error processing {node.Kind}: {ex.Message}", ex);
        }

        if (node.Kind == "Grammar" && _currentResult is GrammarAst grammar)
            Result = grammar;
    }

    private CsNitraAst NamedAlternative(List<CsNitraAst> children, int startPos, int endPos)
    {
        throw new NotImplementedException();
    }

    private CsNitraAst ProcessAssociativity(List<CsNitraAst> children, int startPos, int endPos) => children switch
    {
        [Literal commc, Literal right] => new AssociativityAst(commc, right, startPos, endPos),
        _ => throw new InvalidOperationException($"Expected Associativity: [{string.Join(", ", children)}]"),
    };

    private CsNitraAst ProcessPrecedenceStatement(List<CsNitraAst> children, int startPos, int endPos) => children switch
    {
        [Literal precedenceKw, AstList<Identifier> list, Literal semicolon] =>
            new PrecedenceStatementAst(precedenceKw, list.Items, semicolon, startPos, endPos),
        _ => throw new InvalidOperationException("Expected precedence statement"),
    };

    private CsNitraAst ProcessGrammar(List<CsNitraAst> children, int startPos, int endPos) => children switch
    {
        [AstList<UsingAst> usings, AstList<StatementAst> statements] => new GrammarAst(usings.Items, statements.Items, startPos, endPos),
        _ => throw new InvalidOperationException("Expected Grammar"),
    };

    private CsNitraAst ProcessOpenUsing(List<CsNitraAst> children, int startPos, int endPos) => children switch
    {
        [Literal usingKw, QualifiedIdentifierAst qi, Literal semicolon] => new OpenUsingAst(usingKw, qi, semicolon, startPos, endPos),
        _ => throw new InvalidOperationException("Expected QualifiedIdentifier in OpenUsing")
    };

    private CsNitraAst ProcessAliasUsing(List<CsNitraAst> children, int startPos, int endPos)
    {
        var alias = children[1] as Identifier
            ?? throw new InvalidOperationException("Expected alias identifier");

        var qualifiedId = children[3] as QualifiedIdentifierAst
            ?? throw new InvalidOperationException("Expected QualifiedIdentifier in AliasUsing");

        return new AliasUsingAst(alias.Value, qualifiedId, startPos, endPos);
    }

    private CsNitraAst ProcessNamedAlternative(List<CsNitraAst> children, int startPos, int endPos) => children switch
    {
        [Literal pipe, Identifier name, Literal eq, RuleExpressionAst expression] =>
            new NamedAlternativeAst(pipe, name, eq, expression, startPos, endPos),
        _ => throw new InvalidOperationException("Expected NamedAlternative")
    };

    private CsNitraAst ProcessAnonymousAlternative(List<CsNitraAst> children, int startPos, int endPos) => children switch
    {
        [Literal pipe, QualifiedIdentifierAst ruleRef] => new AnonymousAlternativeAst(pipe, ruleRef, startPos, endPos),
        _ => throw new InvalidOperationException("Expected NamedAlternative")
    };

    private CsNitraAst ProcessRuleStatement(List<CsNitraAst> children, int startPos, int endPos) => children switch
    {
        [
            Identifier ruleName,
            Literal equals,
            AstList<AlternativeAst> alternatives,
            Literal semicolon
        ] =>
            new RuleStatementAst(
                Name: ruleName,
                Eq: equals,
                Alternatives: alternatives.Items,
                StartPos: startPos,
                EndPos: endPos
            ),
        _ => throw new InvalidOperationException($"Invalid rule statement: [{string.Join(", ", children)}]")
    };

    private CsNitraAst ProcessSimpleRuleStatement(List<CsNitraAst> children, int startPos, int endPos) => children switch
    {
        [Identifier name, Literal eq, RuleExpressionAst expression, Literal semicolon] =>
            new SimpleRuleStatementAst(name, eq, expression, semicolon, startPos, endPos),
        _ => throw new InvalidOperationException("Expected SimpleRule statement")
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

    private CsNitraAst ProcessSequenceExpression(List<CsNitraAst> children, int startPos, int endPos)
    {
        var left = children[0] as RuleExpressionAst
            ?? throw new InvalidOperationException("Expected left expression in Sequence");

        var right = children[1] as RuleExpressionAst
            ?? throw new InvalidOperationException("Expected right expression in Sequence");

        return new SequenceExpressionAst(left, right, startPos, endPos);
    }

    private CsNitraAst ProcessNamedExpression(List<CsNitraAst> children, int startPos, int endPos)
    {
        var name = children[0] as Identifier
            ?? throw new InvalidOperationException("Expected name in Named expression");

        var expression = children[2] as RuleExpressionAst
            ?? throw new InvalidOperationException("Expected expression in Named expression");

        return new NamedExpressionAst(name, expression, startPos, endPos);
    }

    private CsNitraAst ProcessOptionalExpression(List<CsNitraAst> children, int startPos, int endPos)
    {
        var expression = children[0] as RuleExpressionAst
            ?? throw new InvalidOperationException("Expected expression in Optional");

        return new OptionalExpressionAst(expression, startPos, endPos);
    }

    private CsNitraAst ProcessOftenMissedExpression(List<CsNitraAst> children, int startPos, int endPos)
    {
        var expression = children[0] as RuleExpressionAst
            ?? throw new InvalidOperationException("Expected expression in OftenMissed");

        return new OftenMissedExpressionAst(expression, startPos, endPos);
    }

    private CsNitraAst ProcessOneOrManyExpression(List<CsNitraAst> children, int startPos, int endPos) => children switch
    {
        [RuleExpressionAst element, Literal plus] => new OneOrManyExpressionAst(element, plus, startPos, endPos),
        _ => throw new InvalidOperationException("Expected expression in ZeroOrMany"),
    };

    private CsNitraAst ProcessZeroOrManyExpression(List<CsNitraAst> children, int startPos, int endPos) => children switch
    {
        [RuleExpressionAst element, Literal star] => new ZeroOrManyExpressionAst(element, star, startPos, endPos),
        _ => throw new InvalidOperationException("Expected expression in ZeroOrMany"),
    };

    private CsNitraAst ProcessAndPredicateExpression(List<CsNitraAst> children, int startPos, int endPos)
    {
        var expression = children[1] as RuleExpressionAst
            ?? throw new InvalidOperationException("Expected expression in AndPredicate");

        return new AndPredicateExpressionAst(expression, startPos, endPos);
    }

    private CsNitraAst ProcessNotPredicateExpression(List<CsNitraAst> children, int startPos, int endPos)
    {
        var expression = children[1] as RuleExpressionAst
            ?? throw new InvalidOperationException("Expected expression in NotPredicate");

        return new NotPredicateExpressionAst(expression, startPos, endPos);
    }

    private CsNitraAst ProcessRuleRefExpression(List<CsNitraAst> children, int startPos, int endPos) => children switch
    {
        [QualifiedIdentifierAst qi, None] => new RuleRefExpressionAst(qi, Precedence: null, startPos, endPos),
        [QualifiedIdentifierAst qi, Some<CsNitraAst>(PrecedenceAst precedence)] => new RuleRefExpressionAst(qi, Precedence: precedence, startPos, endPos),
        _ => throw new InvalidOperationException($"Expected RuleRef expression. But fond [{string.Join(", ", children.Select(x => x!.ToString()))}]")
    };

    private CsNitraAst ProcessPrecedenceWithAssociativity(List<CsNitraAst> children, int startPos, int endPos) => children switch
    {
        [Literal colon, Identifier precedence, Some<CsNitraAst>(AssociativityAst associativity)] => new PrecedenceAst(colon, precedence, associativity, startPos, endPos),
        [Literal colon, Identifier precedence, None] => new PrecedenceAst(colon, precedence, null, startPos, endPos),
        _ => throw new InvalidOperationException($"Expected precedence, associativity. But fond [{string.Join(", ", children.Select(x => x!.ToString()))}]")
    };

    private CsNitraAst ProcessGroupExpression(List<CsNitraAst> children, int startPos, int endPos)
    {
        var expression = children[1] as RuleExpressionAst
            ?? throw new InvalidOperationException("Expected expression in Group");

        return new GroupExpressionAst(expression, startPos, endPos);
    }

    private CsNitraAst ProcessSeparatedListExpression(List<CsNitraAst> children, int startPos, int endPos) => children switch
    {
        [Literal opening, RuleExpressionAst element, Literal semicolon, RuleExpressionAst separator, None, Literal closing, Literal count] =>
            new SeparatedListExpressionAst(element, separator, Modifier: null, count, startPos, endPos),
        [Literal opening, RuleExpressionAst element, Literal semicolon, RuleExpressionAst separator, Some<CsNitraAst>(Literal modifier), Literal closing, Literal count] =>
            new SeparatedListExpressionAst(element, separator, modifier, count, startPos, endPos),
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
            else
                throw new InvalidOperationException($"Expected {typeof(T).Name}. But fond {_currentResult}");
            _currentResult = null;
        }

        return new AstList<T>(items, node.StartPos, node.EndPos);
    }

    private (IReadOnlyList<TElement> Elements, IReadOnlyList<TDelimiter> Delimiters) ProcessAstDelimitedList<TElement, TDelimiter>(ListNode node)
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

        return (elements, delimiters);
    }

    private CsNitraAst ProcessAstList<T>(List<CsNitraAst> children, int startPos, int endPos) where T : CsNitraAst
    {
        var items = new List<T>();
        foreach (var child in children)
            if (child is T item)
                items.Add(item);

        return new AstList<T>(items, startPos, endPos);
    }

    public void Visit(ListNode node) => _currentResult = node.Kind switch
    {
        "QualifiedIdentifier" => ProcessQualifiedIdentifier(node),
        "Precedences" => ProcessAstList<Identifier>(node),
        "Alternatives" => ProcessAlternatives(node),
        _ => ProcessAstList<CsNitraAst>(node),
    };

    private AstList<AlternativeAst> ProcessAlternatives(ListNode node)
    {
        var items = new List<AlternativeAst>();

        foreach (var element in node.Elements)
        {
            element.Accept(this);
            if (_currentResult is AlternativeAst result)
                items.Add(result);
            else
                throw new InvalidOperationException($"Unexpected {_currentResult}");
            _currentResult = null;
        }

        return new AstList<AlternativeAst>(items, node.StartPos, node.EndPos);
    }


    private QualifiedIdentifierAst ProcessQualifiedIdentifier(ListNode node)
    {
        var (parts, delimiters) = ProcessAstDelimitedList<Identifier, Literal>(node);
        if (parts.Count == 0)
        {
        }
        return new QualifiedIdentifierAst(parts, delimiters, node.StartPos, node.EndPos);
    }

    public void Visit(SomeNode node)
    {
        node.Value.Accept(this);
        _currentResult = Option.Some(_currentResult.AssertIsNonNull());
    }

    public void Visit(NoneNode node) => _currentResult = Option.None;

    // Helper classes for visitor
    private record AstList<T>(List<T> Items, int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos) where T : CsNitraAst
    {
        public override void Accept(IAstVisitor visitor)
        {
        }
    }

    private record StringValue(string Value, int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos)
    {
        public override void Accept(IAstVisitor visitor) { }
    }
}
