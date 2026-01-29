using CsNitra.Ast;
using ExtensibleParaser;

namespace CsNitra.Tests;

[TestClass]
public class RuleGeneratorTests
{
    private readonly CsNitraParser _manualParser = new();

    [TestMethod]
    public void GeneratedParserShouldParseGrammarSameAsManualParser()
    {
        var grammarText = GetGrammarText();

        // Шаг 1: Парсим грамматику текущим (ручным) парсером
        var manualParseResult = _manualParser.Parse<GrammarAst>(grammarText);

        if (manualParseResult is Failed(var error))
            Assert.Fail($"Manual parser failed to parse grammar: {error.GetErrorText()}");

        if (manualParseResult is not Success<GrammarAst>(var manualGrammar))
        {
            Assert.Fail("Manual parse result is not success");
            return;
        }

        Assert.IsNotNull(manualGrammar);
        Assert.IsTrue(manualGrammar.Statements.Count > 0, "Grammar should have statements");

        // Шаг 2: Создаем новый парсер используя RuleGenerator
        var generatedParser = new Parser(CsNitraTerminals.Trivia());

        generatedParser.BuildFromAst(manualGrammar, new SourceText(grammarText, "CsNitra.grammar"), terminals: [
            CsNitraTerminals.Identifier(),
            CsNitraTerminals.Literal(),
        ]);

        // Шаг 3: Завершаем построение сгенерированного парсера
        generatedParser.BuildTdoppRules("Grammar");

        // Шаг 4: Тестируем новый парсер на той же грамматике
        var generatedParseResult = generatedParser.Parse(grammarText, startRule: "Grammar", out _);

        if (generatedParser.ErrorInfo is { } errorInfo)
            Assert.Fail(errorInfo.GetErrorText());

        Assert.IsTrue(generatedParseResult.TryGetSuccess(out var generatedNode, out _),
            $"Generated parser failed to parse grammar. Error at pos: {generatedParser.ErrorPos}");

        // Шаг 5: Сравниваем результаты парсинга
        Assert.IsNotNull(generatedNode);

        // Проверяем, что оба парсера парсят один и тот же текст и получают результаты похожей структуры
        var visitor = new CsNitraVisitor(grammarText);
        generatedNode.Accept(visitor);
        var generatedGrammar = (GrammarAst)visitor.Result!;

        // Шаг 6: Сравниваем структуру AST-ов
        AssertGrammarsAreEqual(manualGrammar, generatedGrammar);
    }

    private static void AssertGrammarsAreEqual(GrammarAst manual, GrammarAst generated)
    {
        Assert.AreEqual(manual.Statements.Count, generated.Statements.Count,
            "Количество statements должно совпадать");

        for (int i = 0; i < manual.Statements.Count; i++)
        {
            AssertStatementsAreEqual(manual.Statements[i], generated.Statements[i], i);
        }

        Assert.AreEqual(manual.Usings.Count, generated.Usings.Count,
            "Количество usings должно совпадать");

        for (int i = 0; i < manual.Usings.Count; i++)
        {
            AssertUsingsAreEqual(manual.Usings[i], generated.Usings[i], i);
        }
    }

    private static void AssertStatementsAreEqual(StatementAst manual, StatementAst generated, int index)
    {
        Assert.AreEqual(manual.GetType(), generated.GetType(),
            $"Statement [{index}]: типы должны совпадать");

        Assert.AreEqual(manual.StartPos, generated.StartPos,
            $"Statement [{index}]: StartPos должен совпадать");

        Assert.AreEqual(manual.EndPos, generated.EndPos,
            $"Statement [{index}]: EndPos должен совпадать");

        if (manual is RuleStatementAst manualRule && generated is RuleStatementAst generatedRule)
        {
            Assert.AreEqual(manualRule.Name.Value, generatedRule.Name.Value,
                $"Rule [{index}]: Name должна совпадать");

            Assert.AreEqual(manualRule.Alternatives.Count, generatedRule.Alternatives.Count,
                $"Rule [{manualRule.Name.Value}]: количество alternatives должно совпадать");

            for (int i = 0; i < manualRule.Alternatives.Count; i++)
            {
                AssertAlternativesAreEqual(manualRule.Alternatives[i], generatedRule.Alternatives[i], manualRule.Name.Value, i);
            }
        }
        else if (manual is SimpleRuleStatementAst manualSimple && generated is SimpleRuleStatementAst generatedSimple)
        {
            Assert.AreEqual(manualSimple.Name.Value, generatedSimple.Name.Value,
                $"SimpleRule [{index}]: Name должна совпадать");

            AssertRuleExpressionsAreEqual(manualSimple.Expression, generatedSimple.Expression, $"SimpleRule[{manualSimple.Name.Value}]");
        }
        else if (manual is PrecedenceStatementAst manualPrec && generated is PrecedenceStatementAst generatedPrec)
        {
            Assert.AreEqual(manualPrec.Precedences.Count, generatedPrec.Precedences.Count,
                $"Precedence [{index}]: количество precedences должно совпадать");

            for (int i = 0; i < manualPrec.Precedences.Count; i++)
            {
                Assert.AreEqual(manualPrec.Precedences[i].Value, generatedPrec.Precedences[i].Value,
                    $"Precedence [{index}]: precedence[{i}] должна совпадать");
            }
        }
    }

    private static void AssertAlternativesAreEqual(AlternativeAst manual, AlternativeAst generated, string ruleName, int index)
    {
        Assert.AreEqual(manual.GetType(), generated.GetType(),
            $"Alternative [{ruleName}:{index}]: типы должны совпадать");

        if (manual is NamedAlternativeAst manualNamed && generated is NamedAlternativeAst generatedNamed)
        {
            Assert.AreEqual(manualNamed.Name.Value, generatedNamed.Name.Value,
                $"Alternative [{ruleName}:{index}]: Name должна совпадать");

            AssertRuleExpressionsAreEqual(manualNamed.Expression, generatedNamed.Expression, $"Alternative[{ruleName}:{index}]");
        }
        else if (manual is AnonymousAlternativeAst manualAnon && generated is AnonymousAlternativeAst generatedAnon)
        {
            AssertQualifiedIdentifiersAreEqual(manualAnon.RuleRef, generatedAnon.RuleRef, $"Alternative[{ruleName}:{index}]");
        }
    }

    private static void AssertRuleExpressionsAreEqual(RuleExpressionAst manual, RuleExpressionAst generated, string context)
    {
        Assert.AreEqual(manual.GetType(), generated.GetType(),
            $"RuleExpression [{context}]: типы должны совпадать. Manual: {manual.GetType().Name}, Generated: {generated.GetType().Name}");

        Assert.AreEqual(manual.StartPos, generated.StartPos,
            $"RuleExpression [{context}]: StartPos должен совпадать");

        Assert.AreEqual(manual.EndPos, generated.EndPos,
            $"RuleExpression [{context}]: EndPos должен совпадать");

        switch (manual)
        {
            case LiteralAst manualLit when generated is LiteralAst generatedLit:
                Assert.AreEqual(manualLit.Value, generatedLit.Value,
                    $"Literal [{context}]: значение должно совпадать");
                break;

            case RuleRefExpressionAst manualRef when generated is RuleRefExpressionAst generatedRef:
                AssertQualifiedIdentifiersAreEqual(manualRef.Ref, generatedRef.Ref, context);
                if ((manualRef.Precedence == null) != (generatedRef.Precedence == null))
                {
                    Assert.Fail($"RuleExpression [{context}]: Precedence должен совпадать");
                }
                if (manualRef.Precedence != null && generatedRef.Precedence != null)
                {
                    Assert.AreEqual(manualRef.Precedence.Precedence.Value, generatedRef.Precedence.Precedence.Value,
                        $"RuleExpression [{context}]: Precedence name должен совпадать");
                }
                break;

            case SequenceExpressionAst manualSeq when generated is SequenceExpressionAst generatedSeq:
                AssertRuleExpressionsAreEqual(manualSeq.Left, generatedSeq.Left, $"{context}.Left");
                AssertRuleExpressionsAreEqual(manualSeq.Right, generatedSeq.Right, $"{context}.Right");
                break;

            case NamedExpressionAst manualNamed when generated is NamedExpressionAst generatedNamed:
                Assert.AreEqual(manualNamed.Name, generatedNamed.Name,
                    $"NamedExpression [{context}]: Name должна совпадать");
                AssertRuleExpressionsAreEqual(manualNamed.Expression, generatedNamed.Expression, $"{context}.{manualNamed.Name}");
                break;

            case OptionalExpressionAst manualOpt when generated is OptionalExpressionAst generatedOpt:
                AssertRuleExpressionsAreEqual(manualOpt.Expression, generatedOpt.Expression, $"{context}?");
                break;

            case OneOrManyExpressionAst manualOneOrMany when generated is OneOrManyExpressionAst generatedOneOrMany:
                AssertRuleExpressionsAreEqual(manualOneOrMany.Expression, generatedOneOrMany.Expression, $"{context}+");
                break;

            case ZeroOrManyExpressionAst manualZeroOrMany when generated is ZeroOrManyExpressionAst generatedZeroOrMany:
                AssertRuleExpressionsAreEqual(manualZeroOrMany.Expression, generatedZeroOrMany.Expression, $"{context}*");
                break;

            case AndPredicateExpressionAst manualAnd when generated is AndPredicateExpressionAst generatedAnd:
                AssertRuleExpressionsAreEqual(manualAnd.Expression, generatedAnd.Expression, $"{context}&");
                break;

            case NotPredicateExpressionAst manualNot when generated is NotPredicateExpressionAst generatedNot:
                AssertRuleExpressionsAreEqual(manualNot.Expression, generatedNot.Expression, $"{context}!");
                break;

            case OftenMissedExpressionAst manualOften when generated is OftenMissedExpressionAst generatedOften:
                AssertRuleExpressionsAreEqual(manualOften.Expression, generatedOften.Expression, $"{context}??");
                break;

            case GroupExpressionAst manualGroup when generated is GroupExpressionAst generatedGroup:
                AssertRuleExpressionsAreEqual(manualGroup.Expression, generatedGroup.Expression, $"{context}()");
                break;

            case SeparatedListExpressionAst manualSepList when generated is SeparatedListExpressionAst generatedSepList:
                AssertRuleExpressionsAreEqual(manualSepList.Element, generatedSepList.Element, $"{context}.Element");
                AssertRuleExpressionsAreEqual(manualSepList.Separator, generatedSepList.Separator, $"{context}.Separator");
                if ((manualSepList.Modifier == null) != (generatedSepList.Modifier == null))
                {
                    Assert.Fail($"SeparatedList [{context}]: Modifier должен совпадать");
                }
                if (manualSepList.Modifier != null && generatedSepList.Modifier != null)
                {
                    Assert.AreEqual(manualSepList.Modifier.Value, generatedSepList.Modifier.Value,
                        $"SeparatedList [{context}]: Modifier должен совпадать");
                }
                Assert.AreEqual(manualSepList.Count.Value, generatedSepList.Count.Value,
                    $"SeparatedList [{context}]: Count должен совпадать");
                break;

            default:
                Assert.Fail($"RuleExpression [{context}]: неизвестный тип выражения {manual.GetType().Name}");
                break;
        }
    }

    private static void AssertQualifiedIdentifiersAreEqual(QualifiedIdentifierAst manual, QualifiedIdentifierAst generated, string context)
    {
        Assert.AreEqual(manual.Parts.Count, generated.Parts.Count,
            $"QualifiedIdentifier [{context}]: количество частей должно совпадать");

        for (int i = 0; i < manual.Parts.Count; i++)
        {
            Assert.AreEqual(manual.Parts[i].Value, generated.Parts[i].Value,
                $"QualifiedIdentifier [{context}]: часть[{i}] должна совпадать");
        }
    }

    private static void AssertUsingsAreEqual(UsingAst manual, UsingAst generated, int index)
    {
        Assert.AreEqual(manual.GetType(), generated.GetType(),
            $"Using [{index}]: типы должны совпадать");

        if (manual is OpenUsingAst manualOpen && generated is OpenUsingAst generatedOpen)
        {
            AssertQualifiedIdentifiersAreEqual(manualOpen.QualifiedIdentifier, generatedOpen.QualifiedIdentifier, $"Using[{index}]");
        }
        else if (manual is AliasUsingAst manualAlias && generated is AliasUsingAst generatedAlias)
        {
            Assert.AreEqual(manualAlias.Alias, generatedAlias.Alias,
                $"AliasUsing [{index}]: Alias должна совпадать");
            AssertQualifiedIdentifiersAreEqual(manualAlias.QualifiedIdentifier, generatedAlias.QualifiedIdentifier, $"Using[{index}]");
        }
    }

    private static string GetGrammarText() =>
        """
        Grammar = Usings=Using* Statements=Statement*;

        QualifiedIdentifier = (Identifier; ".")+;

        Using =
            | OpenUsing  = "using" QualifiedIdentifier ";"
            | AliasUsing = "using" Identifier "=" QualifiedIdentifier ";";

        Statement =
            | Precedence = "precedence" Precedences=(Identifier; ",")+ ";"
            | Rule       = Identifier "=" Alternatives=Alternative+ ";"
            | SimpleRule = Identifier "=" RuleExpression ";";

        Alternative =
            | NamedAlternative = "|" Identifier "=" RuleExpression
            | AnonymousAlternative = "|" QualifiedIdentifier;

        precedence Primary, Postfix, Predicate, Naming, Optional, Sequence;

        RuleExpression =
            // prefix rules (Primary)
            | Literal
            | RuleRef       = Ref=QualifiedIdentifier PrecedenceWithAssociativity=(":" Precedence=Identifier Associativity=("," Associativity)?)?
            | Group         = "(" RuleExpression ")"
            | SeparatedList = "(" Element=RuleExpression ";" Separator=RuleExpression SeparatorModifier=(":" Modifier)? ")" Count

            // postfix rules (operators)
            | OftenMissed   = RuleExpression : Postfix "??"
            | OneOrMany     = RuleExpression : Postfix "+"
            | ZeroOrMany    = RuleExpression : Postfix "*"
            | AndPredicate  = "&" RuleExpression : Predicate
            | NotPredicate  = "!" RuleExpression : Predicate
            | Named         = Name=Identifier "=" RuleExpression : Naming
            | Optional      = RuleExpression : Optional "?"
            | Sequence      = Left=RuleExpression : Sequence Right=RuleExpression : Sequence;

        Associativity                 =
            | Left  = "left"
            | Right = "right";

        Modifier =
            | Optional = "?"
            | Required = "!";

        Count =
            | OneOrMeny  = "+"
            | ZeroOrMeny = "*";
        """;
}
