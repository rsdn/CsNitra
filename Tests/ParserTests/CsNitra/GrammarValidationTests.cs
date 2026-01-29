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
        var grammarText = """
            Grammar = Statement*;

            Statement = Identifier "=" UndefinedRule ";";
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
        Assert.IsTrue(errors.Any(e => e.Message.Contains("UndefinedRule")),
            "Should report error for undefined rule reference");
    }
}




