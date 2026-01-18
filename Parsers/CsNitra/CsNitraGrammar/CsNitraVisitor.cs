using ExtensibleParaser;

using System.Text;

namespace CsNitra;

public class CsNitraVisitor : ISyntaxVisitor
{
    private readonly string _input;
    private CsNitraAst? _currentResult;

    public CsNitraAst Result => _currentResult.AssertIsNonNull();

    public CsNitraVisitor(string input)
    {
        _input = input;
    }

    public void Visit(TerminalNode node)
    {
        var value = node.ToString(_input);
        _currentResult = node.Kind switch
        {
            "Identifier" => new TokenValue(value, node.StartPos, node.EndPos),
            "StringLiteral" => new StringValue(UnescapeString(value[1..^1]), value, node.StartPos, node.EndPos),
            "CharLiteral" => new CharValue(UnescapeChar(value[1..^1]), value, node.StartPos, node.EndPos),
            "left" or "right" => new TokenValue(value, node.StartPos, node.EndPos),
            "?" or "!" => new TokenValue(value, node.StartPos, node.EndPos),
            "+" or "*" => new TokenValue(value, node.StartPos, node.EndPos),
            _ => new TokenValue(value, node.StartPos, node.EndPos)
        };
    }

    private static string UnescapeString(string input)
    {
        var result = new StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '\\' && i + 1 < input.Length)
            {
                switch (input[i + 1])
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'v': result.Append('\v'); break;
                    case '0': result.Append('\0'); break;
                    default: result.Append(input[i + 1]); break;
                }
                i++;
            }
            else
            {
                result.Append(input[i]);
            }
        }
        return result.ToString();
    }

    private static char UnescapeChar(string input)
    {
        if (input.Length == 0) return '\0';
        if (input[0] == '\\' && input.Length > 1)
        {
            return input[1] switch
            {
                '\'' => '\'',
                '\\' => '\\',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'b' => '\b',
                'f' => '\f',
                'v' => '\v',
                '0' => '\0',
                _ => input[1]
            };
        }
        return input.Length > 0 ? input[0] : '\0';
    }

    public void Visit(SeqNode node)
    {
        var children = node.Elements
            .Select(e =>
            {
                e.Accept(this);
                return _currentResult!;
            })
            .Where(r => r != null)
            .ToArray();

        _currentResult = node.Kind switch
        {
            "Grammar" => ProcessGrammar(node, children),
            "QualifiedIdentifier" => ProcessQualifiedIdentifier(node, children),
            "OpenUsing" => ProcessOpenUsing(node, children),
            "AliasUsing" => ProcessAliasUsing(node, children),
            "PrecedenceDecl" => ProcessPrecedenceDecl(node, children),
            "Rule" => ProcessRule(node, children),
            "SimpleAlternative" => ProcessSimpleAlternative(node, children),
            "NamedAlternative" => ProcessNamedAlternative(node, children),
            "SequenceExpression" => ProcessSequenceExpression(node, children),
            "NamedExpression" => ProcessNamedExpression(node, children),
            "OptionalExpression" => ProcessOptionalExpression(node, children),
            "OftenMissedExpression" => ProcessOftenMissedExpression(node, children),
            "OneOrManyExpression" => ProcessOneOrManyExpression(node, children),
            "ZeroOrManyExpression" => ProcessZeroOrManyExpression(node, children),
            "AndPredicateExpression" => ProcessAndPredicateExpression(node, children),
            "NotPredicateExpression" => ProcessNotPredicateExpression(node, children),
            "RuleRefExpression" => ProcessRuleRefExpression(node, children),
            "GroupExpression" => ProcessGroupExpression(node, children),
            "SeparatedListExpression" => ProcessSeparatedListExpression(node, children),
            "QualifiedIdentifierPart" => ProcessQualifiedIdentifierPart(node, children),
            _ => new UnknownNode(node.Kind, node.StartPos, node.EndPos)
        };
    }

    private Grammar ProcessGrammar(SeqNode node, CsNitraAst[] children)
    {
        if (children is [ListNodeWrapper usingsList, ListNodeWrapper statementsList])
        {
            var usings = usingsList.Items.OfType<Using>().ToList();
            var statements = statementsList.Items.OfType<Statement>().ToList();
            return new Grammar(node.StartPos, node.EndPos, usings, statements);
        }
        throw new InvalidOperationException("Invalid Grammar structure");
    }

    private QualifiedIdentifier ProcessQualifiedIdentifier(SeqNode node, CsNitraAst[] children)
    {
        if (children is [TokenValue firstId, ListNodeWrapper restParts])
        {
            var parts = new List<(string, int, int)>
            {
                (firstId.Value, firstId.StartPos, firstId.EndPos)
            };

            parts.AddRange(restParts.Items.OfType<QualifiedIdentifierPart>()
                .Select(p => (p.Name, p.StartPos, p.EndPos)));

            return new QualifiedIdentifier(node.StartPos, node.EndPos, parts);
        }
        throw new InvalidOperationException("Invalid QualifiedIdentifier structure");
    }

    private record QualifiedIdentifierPart(string Name, int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);
    private record UnknownNode(string Kind, int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);

    private QualifiedIdentifierPart ProcessQualifiedIdentifierPart(SeqNode node, CsNitraAst[] children)
    {
        if (children is [_, TokenValue id])
        {
            return new QualifiedIdentifierPart(id.Value, id.StartPos, id.EndPos);
        }
        throw new InvalidOperationException("Invalid QualifiedIdentifierPart structure");
    }

    private OpenUsing ProcessOpenUsing(SeqNode node, CsNitraAst[] children)
    {
        if (children is [_, QualifiedIdentifier qid, _])
        {
            return new OpenUsing(node.StartPos, node.EndPos, qid);
        }
        throw new InvalidOperationException("Invalid OpenUsing structure");
    }

    private AliasUsing ProcessAliasUsing(SeqNode node, CsNitraAst[] children)
    {
        if (children is [_, TokenValue alias, _, QualifiedIdentifier qid, _])
        {
            return new AliasUsing(node.StartPos, node.EndPos, alias.Value, qid);
        }
        throw new InvalidOperationException("Invalid AliasUsing structure");
    }

    private PrecedenceDecl ProcessPrecedenceDecl(SeqNode node, CsNitraAst[] children)
    {
        if (children is [_, ListNodeWrapper groupsList, _])
        {
            var groups = groupsList.Items.OfType<TokenValue>()
                .Select(g => (g.Value, g.StartPos, g.EndPos))
                .ToList();

            //return new PrecedenceDecl(node.StartPos, node.EndPos, groups);
        }
        throw new InvalidOperationException("Invalid PrecedenceDecl structure");
    }

    private Rule ProcessRule(SeqNode node, CsNitraAst[] children)
    {
        if (children is [TokenValue ruleName, _, _, ListNodeWrapper alternativesList, _])
        {
            var alternatives = alternativesList.Items.OfType<Alternative>().ToList();
            return new Rule(node.StartPos, node.EndPos, ruleName.Value,
                (ruleName.StartPos, ruleName.EndPos), alternatives);
        }
        throw new InvalidOperationException("Invalid Rule structure");
    }

    private SimpleAlternative ProcessSimpleAlternative(SeqNode node, CsNitraAst[] children)
    {
        if (children is [RuleExpression expression])
        {
            return new SimpleAlternative(node.StartPos, node.EndPos, expression);
        }
        throw new InvalidOperationException("Invalid SimpleAlternative structure");
    }

    private NamedAlternative ProcessNamedAlternative(SeqNode node, CsNitraAst[] children)
    {
        if (children is [TokenValue name, _, ListNodeWrapper expressionsList])
        {
            (int, int)? namePos = name != null ? (name.StartPos, name.EndPos) : null;
            var expressions = expressionsList.Items.OfType<RuleExpression>().ToList();
            return new NamedAlternative(node.StartPos, node.EndPos, name?.Value, namePos, expressions);
        }
        else if (children is [NoneNodeWrapper, _, ListNodeWrapper expressionsList2])
        {
            var expressions = expressionsList2.Items.OfType<RuleExpression>().ToList();
            return new NamedAlternative(node.StartPos, node.EndPos, null, null, expressions);
        }
        throw new InvalidOperationException("Invalid NamedAlternative structure");
    }

    // Обработка RuleExpression вариантов
    private SequenceExpression ProcessSequenceExpression(SeqNode node, CsNitraAst[] children)
    {
        if (children is [RuleExpression left, RuleExpression right])
        {
            return new SequenceExpression(node.StartPos, node.EndPos, left, right, "Sequence");
        }
        throw new InvalidOperationException("Invalid SequenceExpression structure");
    }

    private NamedExpression ProcessNamedExpression(SeqNode node, CsNitraAst[] children)
    {
        if (children is [TokenValue name, _, RuleExpression expression])
        {
            return new NamedExpression(node.StartPos, node.EndPos, name.Value,
                (name.StartPos, name.EndPos), expression, "Named");
        }
        throw new InvalidOperationException("Invalid NamedExpression structure");
    }

    private UnaryPostfixExpression ProcessOptionalExpression(SeqNode node, CsNitraAst[] children)
    {
        if (children is [RuleExpression expression, _])
        {
            return new UnaryPostfixExpression(node.StartPos, node.EndPos, "?", expression, "UnaryPostfix");
        }
        throw new InvalidOperationException("Invalid OptionalExpression structure");
    }

    private UnaryPostfixExpression ProcessOftenMissedExpression(SeqNode node, CsNitraAst[] children)
    {
        if (children is [RuleExpression expression, _])
        {
            return new UnaryPostfixExpression(node.StartPos, node.EndPos, "??", expression, "UnaryPostfix");
        }
        throw new InvalidOperationException("Invalid OftenMissedExpression structure");
    }

    private UnaryPostfixExpression ProcessOneOrManyExpression(SeqNode node, CsNitraAst[] children)
    {
        if (children is [RuleExpression expression, _])
        {
            return new UnaryPostfixExpression(node.StartPos, node.EndPos, "+", expression, "UnaryPostfix");
        }
        throw new InvalidOperationException("Invalid OneOrManyExpression structure");
    }

    private UnaryPostfixExpression ProcessZeroOrManyExpression(SeqNode node, CsNitraAst[] children)
    {
        if (children is [RuleExpression expression, _])
        {
            return new UnaryPostfixExpression(node.StartPos, node.EndPos, "*", expression, "UnaryPostfix");
        }
        throw new InvalidOperationException("Invalid ZeroOrManyExpression structure");
    }

    private UnaryPrefixExpression ProcessAndPredicateExpression(SeqNode node, CsNitraAst[] children)
    {
        if (children is [_, RuleExpression expression])
        {
            return new UnaryPrefixExpression(node.StartPos, node.EndPos, "&", expression, "UnaryPrefix");
        }
        throw new InvalidOperationException("Invalid AndPredicateExpression structure");
    }

    private UnaryPrefixExpression ProcessNotPredicateExpression(SeqNode node, CsNitraAst[] children)
    {
        if (children is [_, RuleExpression expression])
        {
            return new UnaryPrefixExpression(node.StartPos, node.EndPos, "!", expression, "UnaryPrefix");
        }
        throw new InvalidOperationException("Invalid NotPredicateExpression structure");
    }

    private RuleRefExpression ProcessRuleRefExpression(SeqNode node, CsNitraAst[] children)
    {
        if (children is [QualifiedIdentifier qid, ..])
        {
            string? precedence = null;
            (int, int)? precedencePos = null;
            string? associativity = null;
            (int, int)? associativityPos = null;

            if (children is [_, ListNodeWrapper precedenceWrapper])
            {
                var items = precedenceWrapper.Items.ToArray();
                if (items is [_, TokenValue precName, ListNodeWrapper assocWrapper])
                {
                    precedence = precName.Value;
                    precedencePos = (precName.StartPos, precName.EndPos);

                    var assocItems = assocWrapper.Items.ToArray();
                    if (assocItems is [_, TokenValue assocValue])
                    {
                        associativity = assocValue.Value;
                        associativityPos = (assocValue.StartPos, assocValue.EndPos);
                    }
                }
            }
            else if (children is [QualifiedIdentifier qid2])
            {
                return new RuleRefExpression(node.StartPos, node.EndPos, qid2, null, null, null, null);
            }

            return new RuleRefExpression(node.StartPos, node.EndPos, qid, precedence, precedencePos,
                associativity, associativityPos);
        }
        throw new InvalidOperationException("Invalid RuleRefExpression structure");
    }

    private GroupExpression ProcessGroupExpression(SeqNode node, CsNitraAst[] children)
    {
        if (children is [_, RuleExpression expression, _])
        {
            var alternative = new SimpleAlternative(expression.StartPos, expression.EndPos, expression);
            return new GroupExpression(node.StartPos, node.EndPos, [alternative]);
        }
        throw new InvalidOperationException("Invalid GroupExpression structure");
    }

    private SeparatedListExpression ProcessSeparatedListExpression(SeqNode node, CsNitraAst[] children)
    {
        if (children is [_, RuleExpression element, _, RuleExpression separator, .. var rest])
        {
            string? modifier = null;
            (int, int)? modifierPos = null;
            TokenValue? count = null;

            if (rest is [ListNodeWrapper modifierWrapper, _, TokenValue countValue])
            {
                var modifierItems = modifierWrapper.Items.ToArray();
                if (modifierItems is [_, TokenValue modValue])
                {
                    modifier = modValue.Value;
                    modifierPos = (modValue.StartPos, modValue.EndPos);
                }
                count = countValue;
            }
            else if (rest is [_, TokenValue countValue2])
            {
                count = countValue2;
            }

            if (count != null)
            {
                return new SeparatedListExpression(node.StartPos, node.EndPos, element, separator, modifier, modifierPos,
                    count.Value, (count.StartPos, count.EndPos));
            }
        }
        throw new InvalidOperationException("Invalid SeparatedListExpression structure");
    }

    public void Visit(ListNode node)
    {
        var items = new List<CsNitraAst>();
        foreach (var element in node.Elements)
        {
            element.Accept(this);
            if (_currentResult != null)
            {
                items.Add(_currentResult);
            }
        }

        _currentResult = new ListNodeWrapper(items);
    }

    public void Visit(SomeNode node) => node.Value.Accept(this);

    public void Visit(NoneNode node) => _currentResult = new NoneNodeWrapper(node.StartPos, node.EndPos);

    // Вспомогательные классы
    private record TokenValue(string Value, int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);
    private record StringValue(string Value, string RawValue, int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);
    private record CharValue(char Value, string RawValue, int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);
    private record ListNodeWrapper(IReadOnlyList<CsNitraAst> Items) : CsNitraAst(
        Items.Count > 0 ? Items[0].StartPos : 0,
        Items.Count > 0 ? Items[^1].EndPos : 0
    );
    private record NoneNodeWrapper(int StartPos, int EndPos) : CsNitraAst(StartPos, EndPos);
}
