using ExtensibleParaser;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsNitra.Tests;

[TestClass]
public class CsNitraTests
{
    private readonly CsNitraParser _parser = new();

    [TestMethod]
    public void ShouldParseItself()
    {
        var grammarText = GetGrammarText();
        var result = _parser.Parse<GrammarAst>(grammarText);

        if (result is Failed(var error))
            Assert.Fail(error.GetErrorText());

        if (result is not Success<GrammarAst>(var ast))
        {
            Assert.Fail("result is not success");
            return;
        }

        // Проверяем основные структуры
        Assert.IsNotNull(ast);
        Assert.IsTrue(ast.Usings.Count > 0);
        Assert.IsTrue(ast.Statements.Count > 0);

        // Проверяем, что есть правило GrammarAst
        var grammarRule = ast.Statements.OfType<RuleStatementAst>()
            .FirstOrDefault(r => r.Name.Value == "Grammar");
        Assert.IsNotNull(grammarRule);
        
        // Проверяем, что есть правило Using
        var usingRule = ast.Statements.OfType<RuleStatementAst>()
            .FirstOrDefault(r => r.Name.Value == "Using");
        Assert.IsNotNull(usingRule);

        // Проверяем позиции
        Assert.IsTrue(ast.StartPos >= 0);
        Assert.IsTrue(ast.EndPos <= grammarText.Length);
        Assert.IsTrue(ast.EndPos > ast.StartPos);

        // Дополнительные проверки структуры
        foreach (var statement in ast.Statements)
        {
            Assert.IsTrue(statement.StartPos >= ast.StartPos);
            Assert.IsTrue(statement.EndPos <= ast.EndPos);

            if (statement is RuleStatementAst rs)
            {
                Assert.IsFalse(string.IsNullOrEmpty(rs.Name.Value));
                Assert.IsTrue(rs.Alternatives.Count > 0);
            }
        }
    }

    private static string GetGrammarText() =>
        """
        using ExtensibleParaserGrammar.Terminals; // открываем класс Terminals содежащий терминалы для парсера

        precedence Primary, UnaryPrefix, UnaryPostfix, Named, Sequence;

        Grammar = Using* Statement*;

        QualifiedIdentifier = (Identifier; ".")+;

        Using =
            | Open = "using" QualifiedIdentifier ";"
            | Alias = "using" Identifier "=" QualifiedIdentifier ";";

        Statement =
            | Precedence = "precedence" (Identifier; ",")+ ";"
            | Rule = Identifier "=" "|"? (RuleExpression; "|")+ ";";

        RuleExpression =
            | Sequence = Left=RuleExpression : Sequence Right=RuleExpression : Sequence
            | Named = Name=Identifier "=" RuleExpression : Named
            | Optional = RuleExpression "?"
            | OftenMissed = RuleExpression "??"
            | OneOrMany = RuleExpression "+"
            | ZeroOrMany = RuleExpression "*"
            | AndPredicate = "&" RuleExpression : UnaryPrefix
            | NotPredicate = "!" RuleExpression : UnaryPrefix
            | Literal
            | RuleRef = Ref=QualifiedIdentifier (":" Precedence=Identifier ("," Associativity)?)?
            | Group = "(" RuleExpression ")"
            | SeparatedList = "(" RuleExpression ";" RuleExpression SeparatorModifier=(":" Modifier)? ")" Count;

        Associativity = "left" | "right";
        Modifier = "?" | "!";
        Count = "+" | "*";
        
        """;
}
