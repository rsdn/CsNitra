using CSharpGrammar;
using ExtensibleParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

[TestClass]
public class CSharpParserTests
{
    [TestMethod]
    public void Parse_SingleStatement_Succeeds()
    {
        var parser = CreateParser();
        var input = "var x = 1;";

        var result = parser.Parse(input, "Grammar", out var triviaLength);

        Assert.IsNull(parser.Parser.ErrorInfo);
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end),
            $"Expected success, error at pos {parser.Parser.ErrorPos}");
        Assert.IsNotNull(node);
        Assert.AreEqual(input.Length, end);
        Assert.AreEqual(0, triviaLength);
        Assert.AreEqual(0, parser.Parser.RecoveryDiagnostics.Count);
    }

    [TestMethod]
    public void Parse_MultipleStatements_Succeeds()
    {
        var parser = CreateParser();
        var input = "var x = 1; var y = x;";

        var result = parser.Parse(input, "Grammar", out _);

        Assert.IsNull(parser.Parser.ErrorInfo);
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end),
            $"Expected success, error at pos {parser.Parser.ErrorPos}");
        Assert.IsNotNull(node);
        Assert.AreEqual(input.Length, end);
    }

    [TestMethod]
    public void Parse_MalformedStatement_ProducesRecoveryDiagnostics()
    {
        var parser = CreateParser();

        parser.Parse("var x = ;", "Grammar", out _);

        Assert.IsTrue(parser.Parser.RecoveryDiagnostics.Count > 0,
            "Expected recovery diagnostics for malformed input");
    }

    [TestMethod]
    public void Ctor_UnknownTerminal_Throws()
    {
        Assert.ThrowsException<InvalidOperationException>(() => new CSharpParser(
            "Grammar = UnknownTerminal;",
            SmokeTestTerminals.Trivia(),
            [SmokeTestTerminals.Identifier(), SmokeTestTerminals.Number()]));
    }

    private static CSharpParser CreateParser() =>
        new(
            EmbeddedGrammar.LoadCs1Grammar(),
            SmokeTestTerminals.Trivia(),
            [SmokeTestTerminals.Identifier(), SmokeTestTerminals.Number()]);
}
