using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.0 — versioning infrastructure: EmbeddedGrammar.LoadGrammarUpTo + CSharpVersionTestHelper.
// Verifies the version table returns the right grammar set/order and that the merged grammar
// parses end-to-end (a Cs1 program and a Cs11 raw string from the same version-11 parser).
[TestClass]
public class CSharpVersionInfrastructureTests
{
    [TestMethod]
    public void LoadGrammarUpTo_1_YieldsCs1Only()
    {
        var grammars = EmbeddedGrammar.LoadGrammarUpTo(1);
        Assert.AreEqual(1, grammars.Count);
        Assert.AreEqual("Cs1.grammar", grammars[0].Path);
    }

    [TestMethod]
    public void LoadGrammarUpTo_6_YieldsCs1Cs2Cs3Cs6InOrder()
    {
        var grammars = EmbeddedGrammar.LoadGrammarUpTo(6);
        Assert.AreEqual(4, grammars.Count);
        Assert.AreEqual("Cs1.grammar", grammars[0].Path);
        Assert.AreEqual("Cs2.grammar", grammars[1].Path);
        Assert.AreEqual("Cs3.grammar", grammars[2].Path);
        Assert.AreEqual("Cs6.grammar", grammars[3].Path);
    }

    [TestMethod]
    public void LoadGrammarUpTo_11_YieldsCs1Cs2Cs3Cs6Cs11InOrder()
    {
        var grammars = EmbeddedGrammar.LoadGrammarUpTo(11);
        Assert.AreEqual(5, grammars.Count);
        Assert.AreEqual("Cs1.grammar", grammars[0].Path);
        Assert.AreEqual("Cs2.grammar", grammars[1].Path);
        Assert.AreEqual("Cs3.grammar", grammars[2].Path);
        Assert.AreEqual("Cs6.grammar", grammars[3].Path);
        Assert.AreEqual("Cs11.grammar", grammars[4].Path);
    }

    [TestMethod]
    public void LoadGrammarUpTo_4_YieldsOnlyExistingFiles()
    {
        // Cs4 does not exist; only existing files <= 4 are included (Cs1, Cs2, Cs3).
        var grammars = EmbeddedGrammar.LoadGrammarUpTo(4);
        Assert.AreEqual(3, grammars.Count);
        Assert.AreEqual("Cs1.grammar", grammars[0].Path);
        Assert.AreEqual("Cs2.grammar", grammars[1].Path);
        Assert.AreEqual("Cs3.grammar", grammars[2].Path);
    }

    [TestMethod]
    public void CreateParser_11_ParsesCs1Program()
    {
        var parser = CSharpVersionTestHelper.CreateParser(11);
        AssertParsesProgram(parser, "class C { void M() { } }");
    }

    [TestMethod]
    public void CreateParser_11_ParsesCs11RawStringProgram()
    {
        var parser = CSharpVersionTestHelper.CreateParser(11);
        AssertParsesProgram(parser, "class C { string s = \"\"\"abc\"\"\"; }");
    }

    private static void AssertParsesProgram(CSharpParser parser, string input)
    {
        var result = parser.Parse(input, "Grammar", out _);
        var success = result.TryGetSuccess(out var node, out var end)
            && end == input.Length
            && parser.Parser.ErrorInfo is null
            && parser.Parser.RecoveryDiagnostics.Count == 0;

        Assert.IsTrue(success,
            $"Expected «{input}» to fully parse (end={end}/{input.Length}, errorPos={parser.Parser.ErrorPos})");
        Assert.IsNotNull(node);
    }
}
