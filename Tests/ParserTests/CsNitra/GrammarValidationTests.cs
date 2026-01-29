using CsNitra.Ast;
using ExtensibleParaser;

namespace CsNitra.Tests;

[TestClass]
public class GrammarValidationTests
{
    private readonly CsNitraParser _parser = new();

    [TestMethod]
    public void ShouldReportErrorForUndefinedRuleReference()
    {
        var expectedName = "UndefinedRule";
        var grammarText = $"""
            Grammar = Statement*;

            Statement = Identifier "=" {expectedName} ";";
            """;

        var parseResult = _parser.Parse<GrammarAst>(grammarText);

        if (parseResult is not Success<GrammarAst>(var grammar))
        {
            Assert.Fail("Grammar should parse successfully");
            return;
        }

        var terminals = new[] {
            ("Identifier", CsNitraTerminals.Identifier()),
            ("Literal", CsNitraTerminals.Literal())
        };

        var typeChecker = new TypeChecker(new SourceText(grammarText, "test.grammar"), terminals);
        var (diagnostics, _) = typeChecker.CheckGrammar(grammar);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.AreEqual(expected: 1, actual: errors.Count);
        var e = errors[0];
        Assert.IsTrue(e.Message.Contains(expectedName), "Should report error for undefined rule reference");
        var actualName = grammarText[e.Location.Start..e.Location.End];
        Assert.AreEqual(expected: expectedName, actual: actualName);
    }
}




