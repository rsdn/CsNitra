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

        // Step 1: Parse grammar with current (manual) parser
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

        // Step 2: Create new parser using RuleGenerator
        var generatedParser = new Parser(CsNitraTerminals.Trivia());

        generatedParser.BuildFromAst(manualGrammar, new SourceText(grammarText, "CsNitra.grammar"), terminals: [
            CsNitraTerminals.Identifier(),
            CsNitraTerminals.Literal(),
        ]);

        // Step 3: Complete building the generated parser
        generatedParser.BuildTdoppRules("Grammar");

        // Step 4: Test new parser on the same grammar
        var generatedParseResult = generatedParser.Parse(grammarText, startRule: "Grammar", out _);

        if (generatedParser.ErrorInfo is { } errorInfo)
            Assert.Fail(errorInfo.GetErrorText());

        Assert.IsTrue(generatedParseResult.TryGetSuccess(out var generatedNode, out _),
            $"Generated parser failed to parse grammar. Error at pos: {generatedParser.ErrorPos}");

        // Step 5: Compare parsing results
        Assert.IsNotNull(generatedNode);

        // Check that both parsers parse the same text and get similar structure results
        var visitor = new CsNitraVisitor(grammarText);
        generatedNode.Accept(visitor);
        var generatedGrammar = (GrammarAst)visitor.Result!;

        // Step 6: Compare AST structures
        AssertGrammarsAreEqual(manualGrammar, generatedGrammar);
    }

    [TestMethod]
    public void RequiredSubruleNamesAreNotSpecified()
    {
        var grammarText = """
            Using = "using" Identifier*;
            """;

        // Step 1: Parse grammar with current (manual) parser
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

        // Step 2: Create new parser using RuleGenerator
        var generatedParser = new Parser(CsNitraTerminals.Trivia());

        var typeChecker = new TypeChecker(new SourceText(grammarText, "CsNitra.grammar"), terminals: [
            CsNitraTerminals.Identifier(),
            CsNitraTerminals.Literal(),
        ]);
        var (diagnostics, globalScope) = typeChecker.CheckGrammar(manualGrammar);

        Assert.AreEqual(expected: 1, actual: diagnostics.Count);
        var e = diagnostics[0];
        Assert.AreEqual(expected: DiagnosticSeverity.Error, actual: e.Severity);
        Assert.IsTrue(e.Message.Contains("No name for ZeroOrMany"));
        var actualName = grammarText[e.Location.Start..e.Location.End];
        Assert.AreEqual(expected: "Identifier*", actual: actualName);
    }

    private static void AssertGrammarsAreEqual(GrammarAst manual, GrammarAst generated)
    {
        Assert.AreEqual(manual.Statements.Count, generated.Statements.Count,
            "Number of statements must match");

        for (int i = 0; i < manual.Statements.Count; i++)
            AssertStatementsAreEqual(manual.Statements[i], generated.Statements[i], i);

        Assert.AreEqual(manual.Usings.Count, generated.Usings.Count,
            "Number of usings must match");

        for (int i = 0; i < manual.Usings.Count; i++)
            AssertUsingsAreEqual(manual.Usings[i], generated.Usings[i], i);
    }

    private static void AssertStatementsAreEqual(StatementAst manual, StatementAst generated, int index)
    {
        Assert.AreEqual(manual.GetType(), generated.GetType(),
            $"Statement [{index}]: types must match");

        Assert.AreEqual(manual.StartPos, generated.StartPos,
            $"Statement [{index}]: StartPos must match");

        Assert.AreEqual(manual.EndPos, generated.EndPos,
            $"Statement [{index}]: EndPos must match");

        if (manual is RuleStatementAst manualRule && generated is RuleStatementAst generatedRule)
        {
            Assert.AreEqual(manualRule.Name.Value, generatedRule.Name.Value,
                $"Rule [{index}]: Name must match");

            Assert.AreEqual(manualRule.Alternatives.Count, generatedRule.Alternatives.Count,
                $"Rule [{manualRule.Name.Value}]: number of alternatives must match");

            for (int i = 0; i < manualRule.Alternatives.Count; i++)
                AssertAlternativesAreEqual(manualRule.Alternatives[i], generatedRule.Alternatives[i], manualRule.Name.Value, i);
        }
        else if (manual is SimpleRuleStatementAst manualSimple && generated is SimpleRuleStatementAst generatedSimple)
        {
            Assert.AreEqual(manualSimple.Name.Value, generatedSimple.Name.Value,
                $"SimpleRule [{index}]: Name must match");

            AssertRuleExpressionsAreEqual(manualSimple.Expression, generatedSimple.Expression, $"SimpleRule[{manualSimple.Name.Value}]");
        }
        else if (manual is PrecedenceStatementAst manualPrec && generated is PrecedenceStatementAst generatedPrec)
        {
            Assert.AreEqual(manualPrec.Precedences.Count, generatedPrec.Precedences.Count,
                $"Precedence [{index}]: number of precedences must match");

            for (int i = 0; i < manualPrec.Precedences.Count; i++)
                Assert.AreEqual(manualPrec.Precedences[i].Value, generatedPrec.Precedences[i].Value,
                    $"Precedence [{index}]: precedence[{i}] must match");
        }
    }

    private static void AssertAlternativesAreEqual(AlternativeAst manual, AlternativeAst generated, string ruleName, int index)
    {
        Assert.AreEqual(manual.GetType(), generated.GetType(),
            $"Alternative [{ruleName}:{index}]: types must match");

        if (manual is NamedAlternativeAst manualNamed && generated is NamedAlternativeAst generatedNamed)
        {
            Assert.AreEqual(manualNamed.Name.Value, generatedNamed.Name.Value,
                $"Alternative [{ruleName}:{index}]: Name must match");

            AssertRuleExpressionsAreEqual(manualNamed.Expression, generatedNamed.Expression, $"Alternative[{ruleName}:{index}]");
        }
        else if (manual is AnonymousAlternativeAst manualAnon && generated is AnonymousAlternativeAst generatedAnon)
            AssertQualifiedIdentifiersAreEqual(manualAnon.RuleRef, generatedAnon.RuleRef, $"Alternative[{ruleName}:{index}]");
    }

    private static void AssertRuleExpressionsAreEqual(RuleExpressionAst manual, RuleExpressionAst generated, string context)
    {
        Assert.AreEqual(manual.GetType(), generated.GetType(),
            $"RuleExpression [{context}]: types must match. Manual: {manual.GetType().Name}, Generated: {generated.GetType().Name}");

        Assert.AreEqual(manual.StartPos, generated.StartPos,
            $"RuleExpression [{context}]: StartPos must match");

        Assert.AreEqual(manual.EndPos, generated.EndPos,
            $"RuleExpression [{context}]: EndPos must match");

        switch (manual)
        {
            case LiteralAst manualLit when generated is LiteralAst generatedLit:
                Assert.AreEqual(manualLit.Value, generatedLit.Value,
                    $"Literal [{context}]: value must match");
                break;

            case RuleRefExpressionAst manualRef when generated is RuleRefExpressionAst generatedRef:
                AssertQualifiedIdentifiersAreEqual(manualRef.Ref, generatedRef.Ref, context);
                if ((manualRef.Precedence == null) != (generatedRef.Precedence == null))
                    Assert.Fail($"RuleExpression [{context}]: Precedence must match");
                if (manualRef.Precedence != null && generatedRef.Precedence != null)
                    Assert.AreEqual(manualRef.Precedence.Precedence.Value, generatedRef.Precedence.Precedence.Value,
                        $"RuleExpression [{context}]: Precedence name must match");
                break;

            case SequenceExpressionAst manualSeq when generated is SequenceExpressionAst generatedSeq:
                AssertRuleExpressionsAreEqual(manualSeq.Left, generatedSeq.Left, $"{context}.Left");
                AssertRuleExpressionsAreEqual(manualSeq.Right, generatedSeq.Right, $"{context}.Right");
                break;

            case NamedExpressionAst manualNamed when generated is NamedExpressionAst generatedNamed:
                Assert.AreEqual(manualNamed.Name, generatedNamed.Name,
                    $"NamedExpression [{context}]: Name must match");
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
                    Assert.Fail($"SeparatedList [{context}]: Modifier must match");
                if (manualSepList.Modifier != null && generatedSepList.Modifier != null)
                    Assert.AreEqual(manualSepList.Modifier.Value, generatedSepList.Modifier.Value,
                        $"SeparatedList [{context}]: Modifier must match");
                Assert.AreEqual(manualSepList.Count.Value, generatedSepList.Count.Value,
                    $"SeparatedList [{context}]: Count must match");
                break;

            default:
                Assert.Fail($"RuleExpression [{context}]: unknown expression type {manual.GetType().Name}");
                break;
        }
    }

    private static void AssertQualifiedIdentifiersAreEqual(QualifiedIdentifierAst manual, QualifiedIdentifierAst generated, string context)
    {
        Assert.AreEqual(manual.Parts.Count, generated.Parts.Count,
            $"QualifiedIdentifier [{context}]: number of parts must match");

        for (int i = 0; i < manual.Parts.Count; i++)
            Assert.AreEqual(manual.Parts[i].Value, generated.Parts[i].Value,
                $"QualifiedIdentifier [{context}]: part[{i}] must match");
    }

    private static void AssertUsingsAreEqual(UsingAst manual, UsingAst generated, int index)
    {
        Assert.AreEqual(manual.GetType(), generated.GetType(),
            $"Using [{index}]: types must match");

        if (manual is OpenUsingAst manualOpen && generated is OpenUsingAst generatedOpen)
            AssertQualifiedIdentifiersAreEqual(manualOpen.QualifiedIdentifier, generatedOpen.QualifiedIdentifier, $"Using[{index}]");
        else if (manual is AliasUsingAst manualAlias && generated is AliasUsingAst generatedAlias)
        {
            Assert.AreEqual(manualAlias.Alias, generatedAlias.Alias,
                $"AliasUsing [{index}]: Alias must match");
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
