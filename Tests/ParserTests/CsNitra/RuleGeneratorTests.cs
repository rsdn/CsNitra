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
        
        if (manualParseResult is not Success<GrammarAst>(var grammar))
        {
            Assert.Fail("Manual parse result is not success");
            return;
        }
        
        Assert.IsNotNull(grammar);
        Assert.IsTrue(grammar.Statements.Count > 0, "Grammar should have statements");

        // Шаг 2: Создаем новый парсер используя RuleGenerator
        var generatedParser = new Parser(CsNitraTerminals.Trivia());
        
        // Создаем Scope из AST граммтики
        var scope = CreateScopeFromGrammar(grammar, [
            CsNitraTerminals.Identifier(),
            CsNitraTerminals.StringLiteral(),
        ], grammarText);
        
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

    private static Scope CreateScopeFromGrammar(GrammarAst grammar, IEnumerable<Terminal> terminals, string sourceText)
    {
        var globalScope = new Scope(null);
        var source = new SourceText(sourceText, "grammar.csnitra");

        // Добавляем встроенные терминалы
        foreach (var terminal in terminals)
            globalScope.AddSymbol(new TerminalSymbol(
                    new Identifier(terminal.Kind, 0, terminal.Kind.Length),
                    new SourceText(terminal.Kind, "generated"),
                    terminal));
        
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
            | StringLiteral
            | RuleRef       = Ref=QualifiedIdentifier (PrecedenceWithAssociativity=(":" Precedence=Identifier (Associativity=("," Associativity))?))?
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
