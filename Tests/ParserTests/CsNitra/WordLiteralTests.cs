#nullable enable

using ExtensibleParser;

namespace CsNitra;

// Whole-word semantics for identifier-like literals in grammar texts (T1.3.1.1):
// WordLiteral matches its value exactly and only when the raw next char is not an
// identifier-continuation char (letter/digit/'_'); end of input satisfies the boundary.
[TestClass]
public sealed class WordLiteralTests
{
    [TestMethod]
    public void TryMatch_ValueFollowedByIdentifierChar_ReturnsMinusOne()
    {
        var word = new WordLiteral("public");

        Assert.AreEqual(-1, word.TryMatch("publicity", 0));
    }

    [TestMethod]
    public void TryMatch_ValueFollowedBySpace_ReturnsLength()
    {
        var word = new WordLiteral("public");

        Assert.AreEqual(6, word.TryMatch("public x", 0));
    }

    [TestMethod]
    public void TryMatch_ValueFollowedBySemicolon_ReturnsLength()
    {
        var word = new WordLiteral("public");

        Assert.AreEqual(6, word.TryMatch("public;", 0));
    }

    [TestMethod]
    public void TryMatch_ValueAtEndOfInput_ReturnsLength()
    {
        var word = new WordLiteral("public");

        Assert.AreEqual(6, word.TryMatch("public", 0));
    }

    [TestMethod]
    public void TryMatch_ValueFollowedByDigit_ReturnsMinusOne()
    {
        var word = new WordLiteral("public");

        Assert.AreEqual(-1, word.TryMatch("public1", 0));
    }

    [TestMethod]
    public void TryMatch_ValueFollowedByUnderscore_ReturnsMinusOne()
    {
        var word = new WordLiteral("public");

        Assert.AreEqual(-1, word.TryMatch("public_", 0));
    }

    [TestMethod]
    public void TryMatch_StartPosInsideLongerWord_MatchesFromStartPos()
    {
        var word = new WordLiteral("public");

        Assert.AreEqual(6, word.TryMatch("xpublic", 1));
        Assert.AreEqual(-1, word.TryMatch("xpublic", 0));
    }

    [TestMethod]
    public void TryMatch_ValueNotPresent_ReturnsMinusOne()
    {
        var word = new WordLiteral("public");

        Assert.AreEqual(-1, word.TryMatch("publ", 0));
        Assert.AreEqual(-1, word.TryMatch("private", 0));
    }

    [TestMethod]
    public void TryMatch_UnicodeIdentifierCharAfterValue_ReturnsMinusOne()
    {
        var word = new WordLiteral("publіc");

        Assert.AreEqual(-1, word.TryMatch("publіcі", 0));
        Assert.AreEqual(6, word.TryMatch("publіc;", 0));
    }

    [TestMethod]
    public void ToString_And_Kind_BehaveAsLiteral()
    {
        var word = new WordLiteral("public");

        Assert.AreEqual("\"public\"", word.ToString());
        Assert.AreEqual("public", word.Kind);
        Assert.AreEqual("CustomKind", new WordLiteral("public", "CustomKind").Kind);
    }

    [TestMethod]
    public void WordLiteral_IsLiteral_Subtype()
    {
        var word = new WordLiteral("using");

        Assert.IsInstanceOfType(word, typeof(Literal));
    }

    [TestMethod]
    public void Literal_PrefixSemantics_Unchanged()
    {
        var literal = new Literal("using");

        Assert.AreEqual(5, literal.TryMatch("usingx", 0));
    }

    [TestMethod]
    public void GrammarText_KeywordLiteral_ParsesWholeWordOnly()
    {
        var parser = BuildGrammarTextParser(
            "Grammar = Stmt*;\nStmt = \"using\" Identifier \";\";",
            [CsNitraTerminals.Identifier()]);

        var ok = parser.Parse("using x;", "Grammar", out _);

        Assert.IsNull(parser.ErrorInfo);
        Assert.IsTrue(ok.TryGetSuccess(out var node, out var end),
            $"Expected 'using x;' to parse, error at pos {parser.ErrorPos}");
        Assert.IsNotNull(node);
        Assert.AreEqual("using x;".Length, end);
    }

    [TestMethod]
    public void GrammarText_KeywordLiteral_LongerIdentifier_DoesNotParse()
    {
        var parser = BuildGrammarTextParser(
            "Grammar = Stmt*;\nStmt = \"using\" Identifier \";\";",
            [CsNitraTerminals.Identifier()]);

        var bad = parser.Parse("usingx;", "Grammar", out _);

        Assert.IsFalse(bad.TryGetSuccess(out _, out _), "'usingx;' must not parse as 'using x;'");
        Assert.IsNotNull(parser.ErrorInfo);
    }

    private static Parser BuildGrammarTextParser(string text, Terminal[] terminals)
    {
        var parser = new Parser(CsNitraTerminals.Trivia());
        parser.BuildFromTexts([(Text: text, Path: "test.grammar")], terminals);
        parser.BuildTdoppRules();
        return parser;
    }
}
