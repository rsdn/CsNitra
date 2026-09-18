using CSharpGrammar;
using ExtensibleParser;
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
    // Single parameterized rule for all dollar depths D>=1 (Stage 5: context + Repeat).
    private const string Raw = "RawInterpolatedStringLiteral";

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
        AssertFails("$\"{x:{}}\"", Regular);
    }

    [TestMethod]
    public void Regular_EscapeResolvingToBrace_GrammarAcceptsRoslynRejects()
    {
        // D7: the [Regex] escape matches the escape FORM, not the hex VALUE — \u007B
        // (resolves to '{') is accepted where Roslyn rejects it (CS1053). The engine has no
        // hex-value arithmetic (D4 category); reject→accept, invalid code only.
        AssertParses("$\"\\u007B\"", Regular);
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
        AssertParses("$\"\"\"{x}\"\"\"", Raw);
        AssertParses("$\"\"\"abc\"\"\"", Raw);
        AssertParses("$\"\"\"{x:0}\"\"\"", Raw);
        AssertParses("$\"\"\"{ $\"a{b}\" }\"\"\"", Raw);
        AssertParses("$\"\"\"\n{ x }\n\"\"\"", Raw);
        AssertParses("$\"\"\"a\"b\"\"\"", Raw);
        AssertParses("$\"\"\"a\"\"b\"\"\"", Raw);
    }

    [TestMethod]
    public void Raw1_InvalidFragments_Reject()
    {
        // S6 bottom (Wave 1): a trailing quote after a closed hole is absorbed, so this fragment
        // recovers to Success@EOF with a recovery diagnostic instead of being rejected.
        AssertRecoversWithEnd("$\"\"\"{x}\"\"\"\"", Raw);
        AssertFails("$\"\"\"{{x}}\"\"\"\"", Raw);
        AssertFails("$\"\"\"{x\"\"\"\"", Raw);
    }

    [TestMethod]
    public void Raw2_ValidFragments_Parse()
    {
        AssertParses("$$\"\"\"{{x}}\"\"\"", Raw);
        AssertParses("$$\"\"\"{{{x}}}\"\"\"", Raw);
        AssertParses("$$\"\"\"{x}\"\"\"", Raw);
        AssertParses("$$\"\"\"{{x:0}}\"\"\"", Raw);
    }

    [TestMethod]
    public void Raw2_InvalidFragments_Reject()
    {
        AssertFails("$$\"\"\"{{{{x}}}}\"\"\"", Raw);
        AssertFails("$$\"\"\"{{x}\"\"\"", Raw);
        AssertFails("$$\"\"\"{x}}\"\"\"", Raw);
    }

    [TestMethod]
    public void Raw3_ValidFragments_Parse()
    {
        AssertParses("$$$\"\"\"{{{x}}}\"\"\"", Raw);
        AssertParses("$$$\"\"\"{{x}}\"\"\"", Raw);
        AssertParses("$$$\"\"\"{x}\"\"\"", Raw);
        AssertParses("$$$\"\"\"{{{x:0}}}\"\"\"", Raw);
        // K=5 = 2 литер. + дыра; закрывающий ран 5 = дыра (3) + 2 литер.
        AssertParses("$$$\"\"\"..{{{{{43}}}}}..\"\"\"", Raw);
    }

    [TestMethod]
    public void Raw3_InvalidFragments_Reject()
    {
        AssertFails("$$$\"\"\"{{{{{{x}}}}}}\"\"\"", Raw);
        AssertFails("$$$\"\"\"{{{x}}\"\"\"", Raw);
        // Закрывающий ран 6 = 2D → CS9007 (дыра закрывается ровно D, остаток ран ≥ D в контенте).
        AssertFails("$$$\"\"\"..{{{{{43}}}}}}..\"\"\"", Raw);
    }

    // D>=4: was impossible before parameterization (only D=1,2,3 existed as separate rules).
    [TestMethod]
    public void Raw4Plus_ValidFragments_Parse()
    {
        AssertParses("$$$$\"\"\"{{{{x}}}}\"\"\"", Raw);
        AssertParses("$$$$\"\"\"{{{{{x}}}}}\"\"\"", Raw);
        // Закрывающий M=5 = дыра (4) + 1 литер.
        AssertParses("$$$$\"\"\"{{{{x}}}}}\"\"\"", Raw);
        AssertParses("$$$$\"\"\"{{{x}}}\"\"\"", Raw);
        AssertParses("$$$$\"\"\"{{{{x:0}}}}\"\"\"", Raw);
        AssertParses("$$$$$\"\"\"{{{{{x}}}}}\"\"\"", Raw);
        AssertParses("$$$$$\"\"\"{{{{x}}}\"\"\"", Raw);
        AssertParses("$$$$$\"\"\"{x}\"\"\"", Raw);
    }

    [TestMethod]
    public void Raw4Plus_InvalidFragments_Reject()
    {
        // D=4: K=8 >= 2D (CS9006)
        AssertFails("$$$$\"\"\"{{{{{{{{x}}}}}}}}\"\"\"", Raw);
        // D=4: закрывающий M=8 = 2D → дыра (4) + ран 4 = D в контенте (CS9007)
        AssertFails("$$$$\"\"\"{{{{x}}}}}}}}\"\"\"", Raw);
        // D=5: K=5 = D => дыра, но закрывающий M=2 < D (CS9005)
        AssertFails("$$$$$\"\"\"{{{{{x}}}\"\"\"", Raw);
    }

    // Nested context scopes: a raw string with its own dollar depth inside a hole.
    [TestMethod]
    public void NestedRawStrings_DifferentDepths_Parse()
    {
        AssertParses("$$$\"\"\"{{{ $\"\"\"{x}\"\"\" }}}\"\"\"", Raw);
        AssertParses("$$$\"\"\"{{{ $$\"\"\"{{x}}\"\"\" }}}\"\"\"", Raw);
        AssertParses("$$$$\"\"\"{{{{ $$$\"\"\"{{{x}}}\"\"\" }}}}\"\"\"", Raw);
    }

    // Newline-transparency: the global trivia scanner eats newlines between terminals inside a
    // string, so the grammar ACCEPTS input Roslyn rejects (InterpolatedStringGrammar §6.3/§7.5).
    [TestMethod]
    public void NewlineTransparency_GrammarAcceptsRoslynRejects()
    {
        AssertParses("$\"a\nb\"", Regular);
        AssertParses("$\"\"\"a\nb\"\"\"", Raw);
        AssertParses("$\"\"\"\n\"\"\"", Raw);
        AssertParses("$\"\"\"\nabc\ndef \"\"\"", Raw);
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
                (Text: EmbeddedGrammar.LoadCs11Grammar(), Path: "Cs11.grammar"),
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

    private static void AssertRecoversWithEnd(string input, string startRule)
    {
        var parser = CreateParser();
        var result = parser.Parse(input, startRule, out _);
        var reachedEnd = result.TryGetSuccess(out var node, out var end) && end == input.Length;
        var diagnosticCount = parser.Parser.RecoveryDiagnostics.Count;
        var recoveryNodes = node is null ? 0 : CountRecoveryNodes(node);
        var errorPresent = diagnosticCount > 0 || recoveryNodes > 0;

        Assert.IsTrue(
            reachedEnd && errorPresent,
            $"Expected {startRule} to recover to Success@EOF with an error for «{Escape(input)}» " +
            $"(end={end}/{input.Length}, reachedEnd={reachedEnd}, recoveryDiagnostics={diagnosticCount}, recoveryNodes={recoveryNodes}, errorInfo={(parser.Parser.ErrorInfo is null ? "null" : "set")})");
    }

    private static int CountRecoveryNodes(ISyntaxNode node)
    {
        var count = node.IsRecovery ? 1 : 0;
        switch (node)
        {
            case SeqNode seq:
                foreach (var el in seq.RawElements)
                    count += CountRecoveryNodes(el);
                break;
            case ListNode list:
                foreach (var el in list.RawElements)
                    count += CountRecoveryNodes(el);
                foreach (var d in list.Delimiters)
                    count += CountRecoveryNodes(d);
                break;
            case SomeNode some:
                count += CountRecoveryNodes(some.Value);
                break;
        }
        return count;
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace("\r", "\\r")
        .Replace("\n", "\\n")
        .Replace("\t", "\\t");
}
