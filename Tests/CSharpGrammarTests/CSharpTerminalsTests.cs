using CSharpGrammar;
using ExtensibleParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

[TestClass]
public class CSharpTerminalsTests
{
    [TestMethod]
    public void Identifier_LetterOrUnderscoreStart_Matches()
    {
        AssertAll(
            CSharpTerminals.Identifier(),
            [("foo", 3), ("_x", 2)]);
    }

    [TestMethod]
    public void Identifier_UnicodeLetterStart_Matches()
    {
        AssertMatch(CSharpTerminals.Identifier(), "С„", 1);
    }

    [TestMethod]
    public void Identifier_DigitOrEmptyStart_Fails()
    {
        AssertAll(
            CSharpTerminals.Identifier(),
            [("1abc", -1), ("", -1)]);
    }

    [TestMethod]
    public void Identifier_NonWordChar_MatchesPrefixOnly()
    {
        AssertAll(
            CSharpTerminals.Identifier(),
            [("a b", 1), ("a-b", 1)]);
    }

    [TestMethod]
    public void DecimalIntegerLiteral_Digits_Matches()
    {
        AssertAll(
            CSharpTerminals.DecimalIntegerLiteral(),
            [("123", 3), ("0", 1), ("007", 3)]);
    }

    [TestMethod]
    public void DecimalIntegerLiteral_TrailedByLetter_MatchesDigitsOnly()
    {
        AssertMatch(CSharpTerminals.DecimalIntegerLiteral(), "12a", 2);
    }

    [TestMethod]
    public void HexIntegerLiteral_HexDigits_Matches()
    {
        AssertAll(
            CSharpTerminals.HexIntegerLiteral(),
            [("0xFF", 4), ("0x0", 3), ("0Xab", 4)]);
    }

    [TestMethod]
    public void HexIntegerLiteral_MissingOrInvalidDigits_Fails()
    {
        AssertAll(
            CSharpTerminals.HexIntegerLiteral(),
            [("0x", -1), ("0xG", -1), ("10x", -1)]);
    }

    [TestMethod]
    public void OctalIntegerLiteral_OctalDigits_Matches()
    {
        AssertMatch(CSharpTerminals.OctalIntegerLiteral(), "0755", 4);
    }

    [TestMethod]
    public void OctalIntegerLiteral_NonOctalDigitOrBareZero_Fails()
    {
        AssertAll(
            CSharpTerminals.OctalIntegerLiteral(),
            [("08", -1), ("0", -1)]);
    }

    [TestMethod]
    public void IntegerSuffix_ValidSuffixes_Matches()
    {
        AssertAll(
            CSharpTerminals.IntegerSuffix(),
            [("ul", 2), ("L", 1), ("u", 1), ("lu", 2)]);
    }

    [TestMethod]
    public void IntegerSuffix_EmptyOrNonSuffixInput_MatchesEmpty()
    {
        AssertAll(
            CSharpTerminals.IntegerSuffix(),
            [("", 0), ("x", 0)]);
    }

    [TestMethod]
    public void IntegerSuffix_RepeatedSuffixChar_MatchesFirstOnly()
    {
        AssertMatch(CSharpTerminals.IntegerSuffix(), "uu", 1);
    }

    [TestMethod]
    public void DecimalRealLiteral_DotPresent_Matches()
    {
        AssertAll(
            CSharpTerminals.DecimalRealLiteral(),
            [("1.5", 3), ("1.", 2), (".5", 2), ("123.456", 7)]);
    }

    [TestMethod]
    public void DecimalRealLiteral_NoDot_Fails()
    {
        AssertAll(
            CSharpTerminals.DecimalRealLiteral(),
            [("1e5", -1), ("abc", -1)]);
    }

    [TestMethod]
    public void Exponent_DigitAfterE_Matches()
    {
        AssertAll(
            CSharpTerminals.Exponent(),
            [("e5", 2), ("E-3", 3), ("e+1", 3)]);
    }

    [TestMethod]
    public void Exponent_MissingDigits_Fails()
    {
        AssertMatch(CSharpTerminals.Exponent(), "e", -1);
    }

    [TestMethod]
    public void RealSuffix_SingleSuffixChar_Matches()
    {
        AssertAll(
            CSharpTerminals.RealSuffix(),
            [("f", 1), ("D", 1), ("m", 1)]);
    }

    [TestMethod]
    public void RealSuffix_NonSuffixOrEmptyInput_Fails()
    {
        AssertAll(
            CSharpTerminals.RealSuffix(),
            [("x", -1), ("", -1)]);
    }

    [TestMethod]
    public void StringLiteral_QuotedText_Matches()
    {
        AssertAll(
            CSharpTerminals.StringLiteral(),
            [("\"\"", 2), ("\"abc\"", 5), ("\"hello\"", 7), ("\"a\\\"b\"", 6), ("\"\\\\\"", 4), ("\"\\\\n\"", 5)]);
    }

    [TestMethod]
    public void StringLiteral_SimpleEscapes_Matches()
    {
        AssertAll(
            CSharpTerminals.StringLiteral(),
            [
                ("\"\\\"\"", 4),
                ("\"\\\'\"", 4),
                ("\"\\\\\"", 4),
                ("\"\\0\"", 4),
                ("\"\\a\"", 4),
                ("\"\\b\"", 4),
                ("\"\\f\"", 4),
                ("\"\\n\"", 4),
                ("\"\\r\"", 4),
                ("\"\\t\"", 4),
                ("\"\\v\"", 4)
            ]);
    }

    [TestMethod]
    public void StringLiteral_UnicodeEscapes_Matches()
    {
        AssertAll(
            CSharpTerminals.StringLiteral(),
            [
                ("\"\\x4\"", 5),
                ("\"\\x41\"", 6),
                ("\"\\x0041\"", 8),
                ("\"\\u0041\"", 8),
                ("\"\\U00000041\"", 12),
                ("\"\\U00010000\"", 12),
                ("\"\\U00010FFF\"", 12),
                ("\"a\\n\\x41\\u0041\"", 15)
            ]);
    }

    [TestMethod]
    public void StringLiteral_InvalidEscape_Fails()
    {
        AssertAll(
            CSharpTerminals.StringLiteral(),
            [
                ("\"\\$\"", -1),
                ("\"\\q\"", -1),
                ("\"\\e\"", -1),
                ("\"\\x\"", -1),
                ("\"\\xZZ\"", -1),
                ("\"\\u\"", -1),
                ("\"\\u12\"", -1),
                ("\"\\u12G4\"", -1),
                ("\"\\U\"", -1),
                ("\"\\U1234567\"", -1),
                ("\"\\U12345678\"", -1)
            ]);
    }

    [TestMethod]
    public void StringLiteral_UnterminatedOrRawNewline_Fails()
    {
        AssertAll(
            CSharpTerminals.StringLiteral(),
            [
                ("\"abc", -1),
                ("\"a\nb\"", -1),
                ("\"a\rb\"", -1),
                ("\"\\", -1),
                ("\"\\x", -1)
            ]);
    }

    [TestMethod]
    public void StringLiteral_RawStringQuotes_FailsUntilT125()
    {
        AssertMatch(CSharpTerminals.StringLiteral(), "\"\"\"" + "x" + "\"\"\"", -1);
    }

    [TestMethod]
    public void InterpolatedStringLiteral_PrefixForms_Matches()
    {
        AssertAll(
            CSharpTerminals.InterpolatedStringLiteral(),
            [
                (@"$""""", 3),
                (@"$""abc""", 6),
                (@"$@""x{y}z""", 9)
            ]);
    }

    [TestMethod]
    public void InterpolatedStringLiteral_AtPrefixOrder_FailsUntilT124()
    {
        AssertMatch(CSharpTerminals.InterpolatedStringLiteral(), @"@$""x{y}""", -1);
    }

    [TestMethod]
    public void InterpolatedStringLiteral_HolesAndEscapedBraces_Matches()
    {
        AssertAll(
            CSharpTerminals.InterpolatedStringLiteral(),
            [
                (@"$""{{x}}""", 8),
                (@"$""{x}""", 6),
                (@"$""{{{some}}}""", 13),
                (@"$""{a { b } c}""", 14),
                (@"$""{f(a, b)}""", 12),
                (@"$""{[1, 2]}""", 11),
                ("$\"{a\nb}\"", 8),
                ("$\"{a // c\nb}\"", 13),
                (@"$""{ /* c */ x }""", 16),
                (@"$""{ /* c } */ x }""", 18)
            ]);
    }

    [TestMethod]
    public void InterpolatedStringLiteral_NestedLiteralsInHole_Matches()
    {
        AssertAll(
            CSharpTerminals.InterpolatedStringLiteral(),
            [
                (@"$""{s = ""}""}""", 12),
                (@"$""{c = '}'}""", 12),
                (@"$""{ @""a b"" }""", 13),
                (@"$""{ $""a{b}c"" }""", 15),
                (@"$""{ $@""x{y}z"" }""", 16),
                (@"$@""a""""b""", 8)
            ]);
    }

    [TestMethod]
    public void InterpolatedStringLiteral_EscapesInContent_Matches()
    {
        AssertAll(
            CSharpTerminals.InterpolatedStringLiteral(),
            [
                ("$\"a\\\"b\"", 7),
                (@"$""\\n""", 6)
            ]);
    }

    [TestMethod]
    public void InterpolatedStringLiteral_FormatSpecifier_Matches()
    {
        AssertAll(
            CSharpTerminals.InterpolatedStringLiteral(),
            [
                (@"$""{x:0}""", 8),
                (@"$""{x:}""", 7)
            ]);
    }

    [TestMethod]
    public void InterpolatedStringLiteral_InvalidHoleOrBrace_Fails()
    {
        AssertAll(
            CSharpTerminals.InterpolatedStringLiteral(),
            [
                ("$\"}\"", -1),
                ("$\"}{\"", -1),
                ("$\"{a\"", -1),
                ("$\"{a", -1),
                ("$\"{inner \"}\"", -1),
                ("$\"{\"a\" + {b}\"", -1),
                ("$\"{#if X}\"", -1),
                ("$\"{a]b}\"", -1),
                ("$\"{a)b}\"", -1),
                ("$\"{x:{}}\"", -1),
                ("$\"{ /* c } x }\"", -1)
            ]);
    }

    [TestMethod]
    public void InterpolatedStringLiteral_EscapedCurly_Fails()
    {
        AssertAll(
            CSharpTerminals.InterpolatedStringLiteral(),
            [
                ("$\"\\u007B\"", -1),
                ("$\"\\x7D\"", -1),
                ("$\"\\U0000007B\"", -1)
            ]);
    }

    [TestMethod]
    public void InterpolatedStringLiteral_UnterminatedOrRawNewline_Fails()
    {
        AssertAll(
            CSharpTerminals.InterpolatedStringLiteral(),
            [
                ("$\"abc", -1),
                ("$\"a\nb\"", -1),
                ("$\"a\rb\"", -1)
            ]);
    }

    [TestMethod]
    public void InterpolatedStringLiteral_RawInterpolated_FailsUntilT125()
    {
        AssertMatch(CSharpTerminals.InterpolatedStringLiteral(), "$$" + "\"\"\"" + "x" + "\"\"\"", -1);
    }

    [TestMethod]
    public void InterpolatedStringLiteral_BadPrefix_Fails()
    {
        AssertAll(
            CSharpTerminals.InterpolatedStringLiteral(),
            [
                ("$", -1),
                ("$@x", -1),
                ("$x", -1),
                ("x", -1)
            ]);
    }

    [TestMethod]
    public void StringLiteral_AtInterpolatedPrefix_Fails()
    {
        AssertAll(
            CSharpTerminals.StringLiteral(),
            [
                (@"$""abc""", -1),
                ("$", -1)
            ]);
    }

    [TestMethod]
    public void VerbatimStringLiteral_DoubledQuotes_Matches()
    {
        AssertAll(
            CSharpTerminals.VerbatimStringLiteral(),
            [("@\"a\"\"b\"", 7), ("@\"x\"", 4)]);
    }

    [TestMethod]
    public void VerbatimStringLiteral_Unterminated_Fails()
    {
        AssertMatch(CSharpTerminals.VerbatimStringLiteral(), "@\"", -1);
    }

    [TestMethod]
    public void CharLiteral_SingleCharOrEscape_Matches()
    {
        AssertAll(
            CSharpTerminals.CharLiteral(),
            [("'a'", 3), ("'\\''", 4), ("'\\n'", 4)]);
    }

    [TestMethod]
    public void CharLiteral_DoubleBackslashEscape_Fails()
    {
        AssertMatch(CSharpTerminals.CharLiteral(), "'\\\\n'", -1);
    }

    [TestMethod]
    public void CharLiteral_EmptyMultipleOrUnterminated_Fails()
    {
        AssertAll(
            CSharpTerminals.CharLiteral(),
            [("''", -1), ("'ab'", -1), ("'a", -1)]);
    }

    [TestMethod]
    public void Trivia_Whitespace_MatchesRun()
    {
        AssertMatch(CSharpTerminals.Trivia(), "   ", 3);
        AssertMatch(CSharpTerminals.Trivia(), "   ", 0, 3);
    }

    [TestMethod]
    public void Trivia_NonTriviaStart_ReturnsZero()
    {
        AssertMatch(CSharpTerminals.Trivia(), "a", 0);
    }

    [TestMethod]
    public void Trivia_LineComment_MatchesCommentAndTrailingNewline()
    {
        AssertMatch(CSharpTerminals.Trivia(), "// c\nx", 5);
        AssertMatch(CSharpTerminals.Trivia(), "// c\nx", 0, 5);
    }

    [TestMethod]
    public void Trivia_NestedBlockComment_MatchesFullComment()
    {
        AssertMatch(CSharpTerminals.Trivia(), "/* a /* b */ c */", 17);
        AssertMatch(CSharpTerminals.Trivia(), "/* a /* b */ c */", 0, 17);
    }

    [TestMethod]
    public void Trivia_MixedTriviaRun_MatchesMaximalRun()
    {
        const string input = "  // hi\n\t/* a /* b */ c */ x";
        AssertMatch(CSharpTerminals.Trivia(), input, 27);
        AssertMatch(CSharpTerminals.Trivia(), input, 0, 27);
    }

    [TestMethod]
    public void Trivia_UnterminatedBlockComment_MatchesToEndOfInput()
    {
        AssertMatch(CSharpTerminals.Trivia(), "/* a", 4);
        AssertMatch(CSharpTerminals.Trivia(), "/* a", 0, 4);
    }

    [TestMethod]
    public void Trivia_BareBlockCommentOpen_Matches()
    {
        AssertMatch(CSharpTerminals.Trivia(), "/*", 2);
        AssertMatch(CSharpTerminals.Trivia(), "/*", 0, 2);
    }

    [TestMethod]
    public void StartPos_NonZeroStart_MatchesFromThatPosition()
    {
        AssertMatch(CSharpTerminals.DecimalIntegerLiteral(), "  123", 3, 2);
        AssertMatch(CSharpTerminals.Identifier(), "ab c", 1, 3);
        AssertMatch(CSharpTerminals.Trivia(), "x  ", 2, 1);
    }

    [TestMethod]
    public void StartPos_StringLiterals_MatchFromThatPosition()
    {
        AssertMatch(CSharpTerminals.StringLiteral(), "x \"ab\"", 4, 2);
        AssertMatch(CSharpTerminals.InterpolatedStringLiteral(), "x $\"{a}\"", 6, 2);
        AssertMatch(CSharpTerminals.InterpolatedStringLiteral(), "x $\"{{a}}\"", 8, 2);
    }

    [TestMethod]
    public void StartPos_MisalignedStart_Fails()
    {
        AssertMatch(CSharpTerminals.StringLiteral(), "\"ab\"", -1, 1);
        AssertMatch(CSharpTerminals.InterpolatedStringLiteral(), "$\"ab\"", -1, 1);
    }

    private static void AssertAll(Terminal terminal, (string Input, int Expected)[] cases)
    {
        foreach (var (input, expected) in cases)
            AssertMatch(terminal, input, expected);
    }

    private static void AssertMatch(Terminal terminal, string input, int expected, int startPos = 0)
        => Assert.AreEqual(expected, terminal.TryMatch(input, startPos), $"input: {Escape(input)}");

    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace("\r", "\\r")
        .Replace("\n", "\\n")
        .Replace("\t", "\\t");
}
