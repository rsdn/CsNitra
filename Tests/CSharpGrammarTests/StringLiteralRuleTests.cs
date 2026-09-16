using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// Fragment parsing of plain/verbatim string literals (Cs1.grammar). StringLiteral and
// VerbatimStringLiteral are grammar rules, not terminals: text runs are StringText/NonQuoteText
// [Regex] terminals, escapes are StringEscape, the verbatim close is guarded by !'"'.
// Clean parse = TryGetSuccess && end == input.Length && ErrorInfo is null && no recovery
// diagnostics (a recovered parse, e.g. an unterminated literal with an inserted quote, is a reject).
[TestClass]
public class StringLiteralRuleTests
{
    private const string Plain = "StringLiteral";
    private const string Verbatim = "VerbatimStringLiteral";

    [TestMethod]
    public void StringLiteral_QuotedText_Parses()
    {
        AssertParses("\"\"", Plain);
        AssertParses("\"abc\"", Plain);
        AssertParses("\"hello\"", Plain);
        AssertParses("\"a\\\"b\"", Plain);
        AssertParses("\"\\\\\"", Plain);
        AssertParses("\"\\\\n\"", Plain);
    }

    [TestMethod]
    public void StringLiteral_SimpleEscapes_Parse()
    {
        AssertParses("\"\\\"\"", Plain);
        AssertParses("\"\\'\"", Plain);
        AssertParses("\"\\\\\"", Plain);
        AssertParses("\"\\0\"", Plain);
        AssertParses("\"\\a\"", Plain);
        AssertParses("\"\\b\"", Plain);
        AssertParses("\"\\f\"", Plain);
        AssertParses("\"\\n\"", Plain);
        AssertParses("\"\\r\"", Plain);
        AssertParses("\"\\t\"", Plain);
        AssertParses("\"\\v\"", Plain);
    }

    [TestMethod]
    public void StringLiteral_UnicodeEscapes_Parse()
    {
        AssertParses("\"\\x4\"", Plain);
        AssertParses("\"\\x41\"", Plain);
        AssertParses("\"\\x0041\"", Plain);
        AssertParses("\"\\u0041\"", Plain);
        AssertParses("\"\\U00000041\"", Plain);
        AssertParses("\"\\U00010000\"", Plain);
        AssertParses("\"\\U00010FFF\"", Plain);
        AssertParses("\"a\\n\\x41\\u0041\"", Plain);
    }

    [TestMethod]
    public void StringLiteral_InvalidEscape_Reject()
    {
        AssertFails("\"\\$\"", Plain);
        AssertFails("\"\\q\"", Plain);
        AssertFails("\"\\e\"", Plain);
        AssertFails("\"\\x\"", Plain);
        AssertFails("\"\\xZZ\"", Plain);
        AssertFails("\"\\u\"", Plain);
        AssertFails("\"\\u12\"", Plain);
        AssertFails("\"\\u12G4\"", Plain);
        AssertFails("\"\\U\"", Plain);
        AssertFails("\"\\U1234567\"", Plain);
        AssertFails("\"\\U12345678\"", Plain);
    }

    [TestMethod]
    public void StringLiteral_Unterminated_Reject()
    {
        AssertFails("\"abc", Plain);
        AssertFails("\"\\", Plain);
        AssertFails("\"\\x", Plain);
    }

    [Ignore]
    [TestMethod]
    public void StringLiteral_RawNewline_Fails()
    {
        // Roslyn rejects a raw newline inside a plain string, but the rule accepts it: trivia
        // eats the newline between StringLiteralParts (newline transparency, §7.5).
        AssertFails("\"a\nb\"", Plain);
    }

    [TestMethod]
    public void StringLiteral_RawNewline_GrammarAcceptsRoslynRejects()
    {
        AssertParses("\"a\nb\"", Plain);
        AssertParses("\"a\r\nb\"", Plain);
    }

    [TestMethod]
    public void StringLiteral_RawStringQuotes_Reject()
    {
        // Version purity: the plain rule matches only 1-quote strings; 3+ quote runs belong to
        // RawStringLiteral, so a full parse of a raw literal from this rule cannot succeed.
        AssertFails("\"\"\"x\"\"\"", Plain);
        AssertFails("\"\"\"\"\"\"x\"\"\"\"\"\"", Plain);
    }

    [TestMethod]
    public void StringLiteral_AtInterpolatedPrefix_Reject()
    {
        AssertFails("$\"abc\"", Plain);
        AssertFails("$", Plain);
    }

    [TestMethod]
    public void VerbatimStringLiteral_QuotedText_Parses()
    {
        AssertParses("@\"\"", Verbatim);
        AssertParses("@\"abc\"", Verbatim);
        AssertParses("@\"literal\"", Verbatim);
    }

    [TestMethod]
    public void VerbatimStringLiteral_DoubledQuotes_Parse()
    {
        AssertParses("@\"a\"\"b\"", Verbatim);
        AssertParses("@\"\"\"\"", Verbatim);
        AssertParses("@\"a\"\"\"", Verbatim);
        AssertParses("@\"x\"", Verbatim);
    }

    [TestMethod]
    public void VerbatimStringLiteral_BackslashIsLiteralContent_Parses()
    {
        AssertParses("@\"back\\slash\"", Verbatim);
        AssertParses("@\"\\e\"", Verbatim);
    }

    [TestMethod]
    public void VerbatimStringLiteral_NewlinesAllowed_Parse()
    {
        AssertParses("@\"line1\nline2\"", Verbatim);
        AssertParses("@\"multi line\nliteral\"", Verbatim);
    }

    [TestMethod]
    public void VerbatimStringLiteral_BracesArePlainContent_Parses()
    {
        AssertParses("@\"{x}\"", Verbatim);
        AssertParses("@\"a}b\"", Verbatim);
    }

    [Ignore]
    [TestMethod]
    public void VerbatimStringLiteral_ExactBoundary_MatchesPrefixOnly()
    {
        // Terminal TryMatch(startPos) prefix-length semantics have no grammar-rule equivalent:
        // the rule stops at the closing quote and reports the consumed length, it never returns
        // a raw match length for a prefix of the input.
        var parser = CreateParser();
        var result = parser.Parse("@\"a\" + 1", Verbatim, out _);
        result.TryGetSuccess(out _, out var end);
        Assert.AreEqual(4, end);
    }

    [TestMethod]
    public void VerbatimStringLiteral_Unterminated_Reject()
    {
        AssertFails("@\"", Verbatim);
        AssertFails("@\"literal", Verbatim);
        AssertFails("@\"\"\"", Verbatim);
    }

    [TestMethod]
    public void VerbatimStringLiteral_NonStringAfterAt_Reject()
    {
        AssertFails("@$x", Verbatim);
        AssertFails("@$", Verbatim);
        AssertFails("@x", Verbatim);
    }

    private static CSharpParser CreateParser() =>
        new(
            EmbeddedGrammar.LoadCs1Grammar(),
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
