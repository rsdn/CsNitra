using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// Fragment parsing of non-interpolated raw string literals (Cs11.grammar). RawStringLiteral is
// a grammar rule, not a terminal: the opening/closing quote run is the RawQuoteRun [Regex]
// terminal (a run of 3+), a content quote run of 1..2 is RawQuoteContent (hand-written, D1:
// "maximal run 1..2" is inexpressible declaratively), the rest is NonQuoteText.
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

    // === Deviations: the grammar rejects where the old scanner accepted (Stage-5 category) ===

    [TestMethod]
    public void RawStringLiteral_QuoteRun3InContent_N4_GrammarRejectsScannerAccepts()
    {
        // A content quote run of 3..N-1 at N>=4 matches no RawLiteralPart (no context-bounded
        // repetition, D1) — the rule stops at the run and the full parse fails.
        AssertFails("\"\"\"\"  \n\"\"\"\n\"\"\"\"", Raw);
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
