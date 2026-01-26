using ExtensibleParaser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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

        if (manualParseResult is not Success<GrammarAst>(var grammar))
        {
            Assert.Fail("Manual parse result is not success");
            return;
        }

        Assert.IsNotNull(grammar);
        Assert.IsTrue(grammar.Statements.Count > 0, "Grammar should have statements");

        // Шаг 2: Создаем новый парсер используя RuleGenerator
        var generatedParser = new Parser(CsNitraTerminals.Trivia());

        // Добавляем встроенные терминалы в парсер
        generatedParser.Rules["StringLiteral"] = [CsNitraTerminals.StringLiteral()];
        generatedParser.Rules["CharLiteral"] = [CsNitraTerminals.CharLiteral()];
        generatedParser.Rules["Identifier"] = [CsNitraTerminals.Identifier()];

        // Создаем Scope из AST граммтики
        var scope = CreateScopeFromGrammar(grammar, grammarText);

        var ruleGenerator = new RuleGenerator(scope, generatedParser);
        ruleGenerator.GenerateRules();

        // Шаг 3: Завершаем построение сгенерированного парсера
        generatedParser.BuildTdoppRules("Grammar");

        // Шаг 4: Тестируем новый парсер на той же грамматике
        var generatedParseResult = generatedParser.Parse(grammarText, startRule: "Grammar", out _);
        Assert.IsTrue(generatedParseResult.TryGetSuccess(out var generatedNode, out _),
            $"Generated parser failed to parse grammar. Error at pos: {generatedParser.ErrorPos}");

        // Шаг 5: Сравниваем результаты парсинга
        Assert.IsNotNull(generatedNode);

        // Проверяем, что оба парсера парсят один и тот же текст и получают результаты похожей структуры
        var visitor = new CsNitraVisitor(grammarText);
        generatedNode.Accept(visitor);
        var generatedGrammar = (GrammarAst)visitor.Result!;

        Assert.AreEqual(grammar.Statements.Count, generatedGrammar.Statements.Count,
            "Both parsers should parse the same number of statements");
    }

    private static Scope CreateScopeFromGrammar(GrammarAst grammar, string sourceText)
    {
        var globalScope = new Scope(null);
        var source = new SourceText(sourceText, "grammar.csnitra");

        // Добавляем встроенные терминалы
        var builtInTerminals = new[] { "StringLiteral", "CharLiteral", "Identifier" };
        foreach (var terminalName in builtInTerminals)
        {
            var terminalSymbol = new RuleSymbol(
                new Identifier(terminalName, 0, terminalName.Length),
                source,
                null,
                null
            );
            globalScope.AddSymbol(terminalSymbol);
        }

        foreach (var statement in grammar.Statements)
        {
            switch (statement)
            {
                case RuleStatementAst ruleStatement:
                    var ruleSymbol = new RuleSymbol(ruleStatement.Name, source, ruleStatement, null);
                    globalScope.AddSymbol(ruleSymbol);
                    break;

                case SimpleRuleStatementAst simpleStatement:
                    var simpleSymbol = new RuleSymbol(simpleStatement.Name, source, null, simpleStatement);
                    globalScope.AddSymbol(simpleSymbol);
                    break;

                case PrecedenceStatementAst precedenceStatement:
                    // Обработка precedence если необходимо
                    break;
            }
        }

        return globalScope;
    }

    private static string GetGrammarText() =>
        """
        precedence Primary, UnaryPrefix, UnaryPostfix, Named, Sequence;

        Grammar = Using* Statement*;

        QualifiedIdentifier = (Identifier; ".")+;

        Using =
            | Open  = "using" QualifiedIdentifier ";"
            | Alias = "using" Identifier "=" QualifiedIdentifier ";";

        Statement =
            | Precedence = "precedence" (Identifier; ",")+ ";"
            | Rule       = Identifier "=" "|"? (Alternative; "|")+ ";"
            | SimpleRule = Identifier "=" RuleExpression;

        Alternative =
            | Named = Identifier "=" RuleExpression
            | QualifiedIdentifier;

        RuleExpression =
            | Literal
            | Sequence      = Left=RuleExpression : Sequence Right=RuleExpression : Sequence
            | Named         = Name=Identifier "=" RuleExpression : Named
            | Optional      = RuleExpression : UnaryPostfix "?"
            | OftenMissed   = RuleExpression : UnaryPostfix "??"
            | OneOrMany     = RuleExpression : UnaryPostfix "+"
            | ZeroOrMany    = RuleExpression : UnaryPostfix "*"
            | AndPredicate  = "&" RuleExpression : UnaryPrefix
            | NotPredicate  = "!" RuleExpression : UnaryPrefix
            | RuleRef       = Ref=QualifiedIdentifier (":" Precedence=Identifier ("," Associativity)?)?
            | Group         = "(" RuleExpression ")"
            | SeparatedList = "(" RuleExpression ";" RuleExpression SeparatorModifier=(":" Modifier)? ")" Count;

        Literal = StringLiteral | CharLiteral;

        Associativity =
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
