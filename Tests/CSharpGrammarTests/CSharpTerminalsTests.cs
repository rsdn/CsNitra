using CSharpGrammar;
using ExtensibleParser;

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
    public void RawString_ValidLiterals_MatchWholeLiteral()
    {
        AssertAll(
            CSharpTerminals.RawString(),
            [
                ("\"\"\"abc\"\"\"", 9),
                ("\"\"\"a\"b\"\"\"", 9),
                ("\"\"\"a\"\"b\"\"\"", 10),
                ("\"\"\"{x}\"\"\"", 9),
                ("\"\"\"\nabc\n\"\"\"", 11),
                ("\"\"\"a\"\"\"\"", 8),
                ("\"\"\"a\"\"\"\"\"", 9),
                ("\"\"\"\"ab\"\"\"\"", 10),
                ("\"\"\"\"  \n\"\"\"\n\"\"\"\"", 15)]);
    }

    [TestMethod]
    public void RawString_AdjacentLiterals_MatchFirstOnly()
    {
        // Q regression: the old regex matched the whole 19-char input as ONE literal (the
        // content alternative absorbed the 3+ quote runs between the two strings). Now the
        // first literal is 9 chars and the second one matches from position 10.
        const string input = "\"\"\"abc\"\"\" \"\"\"def\"\"\"";
        AssertMatch(CSharpTerminals.RawString(), input, 9);
        AssertMatch(CSharpTerminals.RawString(), input, 9, 10);
    }

    [TestMethod]
    public void RawString_UnterminatedOrEmpty_Fails()
    {
        AssertAll(
            CSharpTerminals.RawString(),
            [
                ("\"\"\"abc", -1),
                ("\"\"\"abc\"\"", -1),
                ("\"\"\"", -1),
                ("\"\"\"\"", -1),
                ("\"\"\"\"\"\"", -1),
                ("\"\"\"\"x\"\"\"", -1),
                ("\"\"\"\"\"\"\"\"\"", -1),
                ("\"\"abc", -1),
                ("abc", -1)]);
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
        AssertMatch(CSharpTerminals.Trivia(), """
            // c
            x
            """, 5);
        AssertMatch(CSharpTerminals.Trivia(), """
            // c
            x
            """, 0, 5);
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

    private static void AssertAll(Terminal terminal, (string Input, int Expected)[] cases)
    {
        foreach (var (input, expected) in cases)
            AssertMatch(terminal, input, expected);
    }

    private static void AssertMatch(Terminal terminal, string input, int expected, int startPos = 0)
        => Assert.AreEqual(expected, terminal.TryMatch(input, startPos), $"input: {Escape(input)}");

    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace("""

        """, "\\r")
        .Replace("""


        """, "\\n")
        .Replace("\t", "\\t");
}
