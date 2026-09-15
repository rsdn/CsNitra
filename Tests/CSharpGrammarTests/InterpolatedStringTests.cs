using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// Fragment parsing of interpolated strings (T3.5.2). The parser is built from the Cs1+Cs6
// merge; each family/level has its own start rule (InterpolatedStringGrammar §6).
// Success = TryGetSuccess && end == input.Length && ErrorInfo is null.
[TestClass]
public class InterpolatedStringTests
{
    private const string Regular = "InterpolatedStringLiteral";
    private const string Verbatim = "VerbatimInterpolatedStringLiteral";
    private const string Raw1 = "RawInterpolatedStringLiteral";
    private const string Raw2 = "RawInterpolatedStringLiteral2";
    private const string Raw3 = "RawInterpolatedStringLiteral3";

    [TestMethod]
    public void Regular_ValidFragments_Parse()
    {
        AssertParses("$\"\"", Regular);
        AssertParses("$\"abc\"", Regular);
        AssertParses("$\"{x}\"", Regular);
        AssertParses("$\"{{x}}\"", Regular);
        AssertParses("$\"a {x} b\"", Regular);
        AssertParses("$\"{x:0}\"", Regular);
        AssertParses("$\"{x:}\"", Regular);
        AssertParses("$\"{s = \"a\"}\"", Regular);
        AssertParses("$\"{ $\"a{b}c\" }\"", Regular);
        AssertParses("$\"{ (a + (b * c)) }\"", Regular);
        AssertParses("$\"{ s = \"a } b\" }\"", Regular);
        AssertParses("$\"{(a ? \"y\" : \"n\")}\"", Regular);
    }

    [TestMethod]
    public void Regular_EscapesInContent_Parse()
    {
        AssertParses("$\"a\\\"b\"", Regular);
        AssertParses("$\"\\n\"", Regular);
        AssertParses("$\"\\x41\"", Regular);
        AssertParses("$\"\\u0041\"", Regular);
    }

    [TestMethod]
    public void Regular_InvalidFragments_Reject()
    {
        AssertFails("$\"}\"", Regular);
        AssertFails("$\"{a\"", Regular);
        AssertFails("$\"abc", Regular);
        AssertFails("$\"\\u007B\"", Regular);
        AssertFails("$\"{x:{}}\"", Regular);
    }

    [TestMethod]
    public void Verbatim_ValidFragments_Parse()
    {
        AssertParses("$@\"\"", Verbatim);
        AssertParses("@$\"\"", Verbatim);
        AssertParses("$@\"{x}\"", Verbatim);
        AssertParses("$@\"{{x}}\"", Verbatim);
        AssertParses("$@\"\"\"\"", Verbatim);
        AssertParses("$@\"{x:0}\"", Verbatim);
        AssertParses("$@\"a\\nb\"", Verbatim);
        AssertParses("$@\"{ @\"a}b\" }\"", Verbatim);
    }

    [TestMethod]
    public void Verbatim_InvalidFragments_Reject()
    {
        AssertFails("$@\"{x\"", Verbatim);
        AssertFails("$@\"}\"", Verbatim);
        AssertFails("$@\"a", Verbatim);
    }

    [TestMethod]
    public void Raw1_ValidFragments_Parse()
    {
        AssertParses("$\"\"\"{x}\"\"\"", Raw1);
        AssertParses("$\"\"\"abc\"\"\"", Raw1);
        AssertParses("$\"\"\"{x:0}\"\"\"", Raw1);
        AssertParses("$\"\"\"{ $\"a{b}\" }\"\"\"", Raw1);
        AssertParses("$\"\"\"\n{ x }\n\"\"\"", Raw1);
        AssertParses("$\"\"\"a\"b\"\"\"", Raw1);
        AssertParses("$\"\"\"a\"\"b\"\"\"", Raw1);
    }

    [TestMethod]
    public void Raw1_InvalidFragments_Reject()
    {
        AssertFails("$\"\"\"{x}\"\"\"\"", Raw1);
        AssertFails("$\"\"\"{{x}}\"\"\"", Raw1);
        AssertFails("$\"\"\"{x\"\"\"\"", Raw1);
    }

    [TestMethod]
    public void Raw2_ValidFragments_Parse()
    {
        AssertParses("$$\"\"\"{{x}}\"\"\"", Raw2);
        AssertParses("$$\"\"\"{{{x}}}\"\"\"", Raw2);
        AssertParses("$$\"\"\"{x}\"\"\"", Raw2);
        AssertParses("$$\"\"\"{{x:0}}\"\"\"", Raw2);
    }

    [TestMethod]
    public void Raw2_InvalidFragments_Reject()
    {
        AssertFails("$$\"\"\"{{{{x}}}}\"\"\"", Raw2);
        AssertFails("$$\"\"\"{{x}\"\"\"", Raw2);
        AssertFails("$$\"\"\"{x}}\"\"\"", Raw2);
    }

    [TestMethod]
    public void Raw3_ValidFragments_Parse()
    {
        AssertParses("$$$\"\"\"{{{x}}}\"\"\"", Raw3);
        AssertParses("$$$\"\"\"{{x}}\"\"\"", Raw3);
        AssertParses("$$$\"\"\"{x}\"\"\"", Raw3);
        AssertParses("$$$\"\"\"{{{x:0}}}\"\"\"", Raw3);
    }

    [TestMethod]
    public void Raw3_InvalidFragments_Reject()
    {
        AssertFails("$$$\"\"\"{{{{{{x}}}}}}\"\"\"", Raw3);
        AssertFails("$$$\"\"\"{{{x}}\"\"\"", Raw3);
    }

    // Newline-transparency: the global trivia scanner eats newlines between terminals inside a
    // string, so the grammar ACCEPTS input Roslyn rejects (InterpolatedStringGrammar §6.3/§7.5).
    [TestMethod]
    public void NewlineTransparency_GrammarAcceptsRoslynRejects()
    {
        AssertParses("$\"a\nb\"", Regular);
        AssertParses("$\"\"\"a\nb\"\"\"", Raw1);
        AssertParses("$\"\"\"\n\"\"\"", Raw1);
        AssertParses("$\"\"\"\nabc\ndef \"\"\"", Raw1);
    }

    // §3.1: a top-level conditional without parens is parsed as a conditional by the grammar,
    // but Roslyn treats ':' as the format start (error). Documented discrepancy of the temporary
    // Expression; after T2.3 (bounded HoleExpression) rejection is expected.
    [TestMethod]
    public void ConditionalWithoutParens_GrammarAcceptsRoslynRejects()
    {
        AssertParses("$\"{a ? \"y\" : \"n\"}\"", Regular);
    }

    private static CSharpParser CreateParser() =>
        new(
            [
                (Text: EmbeddedGrammar.LoadCs1Grammar(), Path: "Cs1.grammar"),
                (Text: EmbeddedGrammar.LoadCs6Grammar(), Path: "Cs6.grammar"),
            ],
            CSharpTerminals.Trivia(),
            CSharpTerminals.GetAll());

    private static void AssertParses(string input, string startRule)
    {
        var parser = CreateParser();
        var result = parser.Parse(input, startRule, out _);
        var success = result.TryGetSuccess(out _, out var end) && end == input.Length && parser.Parser.ErrorInfo is null;

        Assert.IsTrue(
            success,
            $"Expected {startRule} to fully parse «{Escape(input)}» (end={end}/{input.Length}, errorPos={parser.Parser.ErrorPos})");
    }

    private static void AssertFails(string input, string startRule)
    {
        var parser = CreateParser();
        var result = parser.Parse(input, startRule, out _);
        var success = result.TryGetSuccess(out _, out var end) && end == input.Length && parser.Parser.ErrorInfo is null;

        Assert.IsFalse(
            success,
            $"Expected {startRule} to reject «{Escape(input)}» (end={end}/{input.Length})");
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace("\r", "\\r")
        .Replace("\n", "\\n")
        .Replace("\t", "\\t");
}
