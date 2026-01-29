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

        var typeChecker = new TypeChecker(new SourceText(grammarText, "test.grammar"), terminals: [
            CsNitraTerminals.Identifier(),
            CsNitraTerminals.Literal(),
        ]);
        var (diagnostics, _) = typeChecker.CheckGrammar(grammar);
        Assert.AreEqual(expected: 1, actual: diagnostics.Count);
        var e = diagnostics[0];
        Assert.AreEqual(expected: DiagnosticSeverity.Error, actual: e.Severity);
        Assert.IsTrue(e.Message.Contains(expectedName), "Should report error for undefined rule reference");
        var actualName = grammarText[e.Location.Start..e.Location.End];
        Assert.AreEqual(expected: expectedName, actual: actualName);
    }
}




