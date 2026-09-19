using CSharpGrammar;
using ExtensibleParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// Fragment parsing of non-interpolated raw string literals (Cs11.grammar). RawStringLiteral
// wraps the hand-written RawString terminal (CSharpTerminals.cs): one match = the maximal
// opening quote run N >= 3, content, then the FIRST quote run of length >= N (the whole run
// is the close; an empty body is impossible by construction).
// Clean parse = TryGetSuccess && end == input.Length && ErrorInfo is null && no recovery
// diagnostics (a recovered parse, e.g. an unterminated literal with an inserted quote run, is
// a reject).
[TestClass]
public class RawStringLiteralRuleTests
{
    private const string Raw = "RawStringLiteral";

    [TestMethod]
    public void RawStringLiteral_QuotedText_Parses()
    {
        AssertParses("\"\"\"abc\"\"\"", Raw);
        AssertParses("\"\"\"a\"b\"\"\"", Raw);
        AssertParses("\"\"\"a\"\"b\"\"\"", Raw);
        AssertParses("\"\"\" \"\"\"", Raw);
        AssertParses("\"\"\"\t\"\"\"", Raw);
        AssertParses("\"\"\"{x}\"\"\"", Raw);
        AssertParses("\"\"\"\"ab\"\"\"\"", Raw);
    }

    [TestMethod]
    public void RawStringLiteral_MultiLine_Parses()
    {
        AssertParses("\"\"\"\nabc\n\"\"\"", Raw);
        AssertParses("\"\"\"\n\n\"\"\"", Raw);
        AssertParses("\"\"\"\r\nabc\r\n\"\"\"", Raw);
        AssertParses("\"\"\"\n  \n\"\"\"", Raw);
        AssertParses("\"\"\"\r\n  abc\r\n     def\r\n  \"\"\"", Raw);
        AssertParses("\"\"\"\n\"\"\n\"\"\"", Raw);
        AssertParses("\"\"\"  \na\"\"\"", Raw);
    }

    [TestMethod]
    public void RawStringLiteral_CloseRunRules_Parses()
    {
        // A quote run >= 3 ends the string and the ENTIRE run is the close (Roslyn keeps the
        // excess quotes in the token; CS8998/CS9000 are semantic errors we do not emit).
        AssertParses("\"\"\"a\"\"\"\"", Raw);
        AssertParses("\"\"\"a\"\"\"\"\"", Raw);
        AssertParses("\"\"\"\nabc\n\"\"\"\"\"", Raw);
    }

    [TestMethod]
    public void RawStringLiteral_UnterminatedOrEmpty_Reject()
    {
        AssertFails("\"\"\"abc\"\"", Raw);
        AssertFails("\"\"\"abc", Raw);
        AssertFails("\"\"\"", Raw);
        AssertFails("\"\"\"\"", Raw);
        AssertFails("\"\"\"\"\"\"", Raw);
        AssertFails("\"\"\"\nabc", Raw);
        AssertFails("\"\"abc\"\"", Raw);
        AssertFails("\"ab\"\"", Raw);
        AssertFails("abc", Raw);
    }

    [TestMethod]
    public void RawStringLiteral_NonRawPrefix_Reject()
    {
        AssertFails("$\"\"\"x\"\"\"", Raw);
        AssertFails("@\"\"\"x\"\"\"", Raw);
        AssertFails("x", Raw);
    }

    // === Deviations: the grammar accepts where the old scanner / Roslyn rejects (D2) ===

    [TestMethod]
    public void RawStringLiteral_RawNewlineInSingleLine_GrammarAcceptsRoslynRejects()
    {
        // Roslyn rejects a raw newline inside a single-line raw string, but the rule accepts
        // it: trivia eats the newline between RawLiteralParts (newline transparency, D2).
        AssertParses("\"\"\"abc\ndef\"\"\"", Raw);
    }

    [TestMethod]
    public void RawStringLiteral_EmptyMultiLine_GrammarAcceptsRoslynRejects()
    {
        // Roslyn CS9002: a multi-line raw string must contain at least one content line; the
        // rule accepts an empty body (trivia eats the newline, loop = 0 parts).
        AssertParses("\"\"\"\n\"\"\"", Raw);
        AssertParses("\"\"\"\n  \"\"\"", Raw);
    }

    [TestMethod]
    public void RawStringLiteral_QuoteRun3InContent_N4_Parses()
    {
        // A content quote run of 3..N-1 at N>=4 is legal content (only a run >= N closes the
        // string) — scanner-consistent (scanner: 15).
        AssertParses("\"\"\"\"  \n\"\"\"\n\"\"\"\"", Raw);
    }

    // === Q: adjacent raw strings must NOT merge into one literal ===

    [TestMethod]
    public void RawStringLiteral_AdjacentLiterals_TwoLiterals()
    {
        // The old regex terminal absorbed the first literal's closing run and the second
        // literal's opening run into the content — one literal of length 19. Now each literal
        // is a separate match: the first spans [0, 9), the second [10, 19).
        var parser = CreateParser();
        const string input = "\"\"\"abc\"\"\" \"\"\"def\"\"\"";

        var first = parser.Parser.ParseSubRule(input, Raw, 0);
        Assert.IsTrue(first.TryGetSuccess(out var firstNode, out _),
            $"Expected the first literal to match, errorPos={parser.Parser.ErrorPos}");
        var firstTerminal = AsTerminal(firstNode, "first");
        Assert.AreEqual(0, firstTerminal.StartPos);
        Assert.AreEqual(9, firstTerminal.ContentLength);

        var second = parser.Parser.ParseSubRule(input, Raw, 10);
        Assert.IsTrue(second.TryGetSuccess(out var secondNode, out _),
            $"Expected the second literal to match, errorPos={parser.Parser.ErrorPos}");
        var secondTerminal = AsTerminal(secondNode, "second");
        Assert.AreEqual(10, secondTerminal.StartPos);
        Assert.AreEqual(9, secondTerminal.ContentLength);
    }

    [TestMethod]
    public void RawStringLiteral_TwoQuoteRunInContent_OneLiteral()
    {
        // Regression: a 2-quote run inside the content is legal — one literal of length 10.
        var parser = CreateParser();
        const string input = "\"\"\"a\"\"b\"\"\"";

        var result = parser.Parser.ParseSubRule(input, Raw, 0);
        Assert.IsTrue(result.TryGetSuccess(out var node, out _),
            $"Expected the literal to match, errorPos={parser.Parser.ErrorPos}");
        var terminal = AsTerminal(node, "literal");
        Assert.AreEqual(0, terminal.StartPos);
        Assert.AreEqual(10, terminal.ContentLength);
    }

    [TestMethod]
    public void RawStringLiteral_Simple_OneLiteralOfLength9()
    {
        var parser = CreateParser();
        const string input = "\"\"\"abc\"\"\"";

        var result = parser.Parser.ParseSubRule(input, Raw, 0);
        Assert.IsTrue(result.TryGetSuccess(out var node, out _),
            $"Expected the literal to match, errorPos={parser.Parser.ErrorPos}");
        var terminal = AsTerminal(node, "literal");
        Assert.AreEqual(0, terminal.StartPos);
        Assert.AreEqual(9, terminal.ContentLength);
    }

    [Ignore]
    [TestMethod]
    public void RawStringLiteral_CloseRunPrefix_MatchLengthSemantics()
    {
        // Terminal TryMatch(startPos) prefix-length semantics have no grammar-rule equivalent:
        // the rule stops at the closing run (the whole run, scanner-consistent length), it
        // never reports a raw match length for a prefix of the input.
        var parser = CreateParser();
        var result = parser.Parse("\"\"\"a\"\"\"\"b\"\"\"", Raw, out _);
        result.TryGetSuccess(out _, out var end);
        Assert.AreEqual(8, end);
    }

    private static TerminalNode AsTerminal(ISyntaxNode? node, string what)
    {
        Assert.IsNotNull(node, $"{what} node is null");
        Assert.IsTrue(node is TerminalNode, $"{what} node is {node.GetType().Name}");
        return (TerminalNode)node;
    }

    private static CSharpParser CreateParser() =>
        new(
            [
                (Text: EmbeddedGrammar.LoadCs1Grammar(), Path: "Cs1.grammar"),
                (Text: EmbeddedGrammar.LoadCs11Grammar(), Path: "Cs11.grammar"),
            ],
            CSharpTerminals.Trivia(),
            CSharpTerminals.GetAll());

    private static bool TryCleanParse(CSharpParser parser, string input, string startRule, out int end)
    {
        var result = parser.Parse(input, startRule, out _);
        end = result.TryGetSuccess(out _, out var successEnd) ? successEnd : -1;

        return end == input.Length
            && parser.Parser.ErrorInfo is null
            && parser.Parser.RecoveryDiagnostics.Count == 0;
    }

    private static void AssertParses(string input, string startRule)
    {
        var parser = CreateParser();
        Assert.IsTrue(
            TryCleanParse(parser, input, startRule, out var end),
            $"Expected {startRule} to cleanly parse «{Escape(input)}» (end={end}/{input.Length}, errorPos={parser.Parser.ErrorPos}, diagnostics=[{string.Join("; ", parser.Parser.RecoveryDiagnostics)}])");
    }

    private static void AssertFails(string input, string startRule)
    {
        var parser = CreateParser();
        Assert.IsFalse(
            TryCleanParse(parser, input, startRule, out var end),
            $"Expected {startRule} to reject «{Escape(input)}» (end={end}/{input.Length})");
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace("\r", "\\r")
        .Replace("\n", "\\n")
        .Replace("\t", "\\t");
}
