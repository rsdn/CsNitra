using CSharpGrammar;
using ExtensibleParser;
using ExtensibleParser.Recovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

[TestClass]
public sealed class RecoveryStringBoundaryTests
{
    [DataTestMethod]
    [DataRow("\"a;\\\"b\"")]
    [DataRow("'{'")]
    [DataRow("@\"a;\"\"b\"")]
    [DataRow("\"\"\"a;{}\"\"\"")]
    [DataRow("$\"{M(\";\")} x\"")]
    [DataRow("$\"\"\"a;{x}\"\"\"")]
    public void CompleteStringIsOpaque(string text)
    {
        var host = new CSharpParser([
            (EmbeddedGrammar.LoadCs1Grammar(), "Cs1.grammar"),
            (EmbeddedGrammar.LoadCs6Grammar(), "Cs6.grammar"),
            (EmbeddedGrammar.LoadCs11Grammar(), "Cs11.grammar")],
            CSharpTerminals.Trivia(), CSharpTerminals.GetAll());
        Assert.AreEqual(text.Length, host.Parser.RecoveryOpaqueTerminals.Max(t => t.TryMatch(text + ";", 0)));
    }

    [DataTestMethod]
    [DataRow("$ \"{ ; }\" ;")]
    [DataRow("$ '{' ;")]
    [DataRow("$ /* ; } */ ;")]
    [DataRow("$ // ; }\n ;")]
    [DataRow("$ \"unterminated ;\n ;")]
    [DataRow("$ $\"{M(\";\")}\" ;")]
    public void BothStrategiesUseCSharpLexicalBoundaries(string input)
    {
        var host = new CSharpParser([
            (EmbeddedGrammar.LoadCs1Grammar(), "Cs1.grammar"),
            (EmbeddedGrammar.LoadCs6Grammar(), "Cs6.grammar"),
            (EmbeddedGrammar.LoadCs11Grammar(), "Cs11.grammar")],
            CSharpTerminals.Trivia(), CSharpTerminals.GetAll());
        var parser = host.Parser;
        parser.Rules["S"] = [new Seq([new Literal("a"),
            new Literal("b"), new Literal(";")], "S")];
        parser.BuildTdoppRules();
        var snapshot = new FailureSnapshot(0,
            [new StackFrame("S", 0, new SeqFrameLocation(0), [],
                new RecoveryOptions { Terminators = [new Literal(";")] })],
            new Literal("a"), []);
        var candidates = RecoveryEngine.Generate(0, snapshot, input, parser,
            Result.Kind.Failure, "S", 0, 0);
        Assert.IsTrue(candidates.Any(c => c.Rank == 3));
        Assert.IsTrue(candidates.Any(c => c.Rank == 6));
        foreach (var candidate in candidates.Where(c => c.Rank is 3 or 6))
            Assert.AreEqual(input.LastIndexOf(';'), candidate.Pos, $"S{candidate.Rank}");
    }

    [DataTestMethod]
    [DataRow("\"\"\"unterminated\n ; }\n class Later {}")]
    [DataRow("$\"\"\"unterminated\n ; }\n class Later {}")]
    public void MalformedRawStringCannotFallBackToAnEmptyRegularString(string text)
    {
        var host = new CSharpParser([
            (EmbeddedGrammar.LoadCs1Grammar(), "Cs1.grammar"),
            (EmbeddedGrammar.LoadCs6Grammar(), "Cs6.grammar"),
            (EmbeddedGrammar.LoadCs11Grammar(), "Cs11.grammar")],
            CSharpTerminals.Trivia(), CSharpTerminals.GetAll());
        Assert.AreEqual(text.Length, host.Parser.RecoveryOpaqueTerminals.Max(t => t.TryMatch(text, 0)));
    }
    [TestMethod]
    public void OpaqueExtentUsesTheCurrentGrammar()
    {
        var host = new CSharpParser(EmbeddedGrammar.LoadCs1Grammar(),
            CSharpTerminals.Trivia(), CSharpTerminals.GetAll());
        const string text = "\"custom\n ; }END";
        host.Parser.Rules["StringLiteral"] = [new Seq([new Literal(text)], "StringLiteral")];
        host.Parser.BuildTdoppRules();
        Assert.IsTrue(host.Parser.ParseSubRule(text, "StringLiteral", 0).TryGetSuccess(out _, out var end));
        Assert.AreEqual(text.Length, end);
        Assert.AreEqual(text.Length, host.Parser.RecoveryOpaqueTerminals.Max(t => t.TryMatch(text, 0)));
    }
}
