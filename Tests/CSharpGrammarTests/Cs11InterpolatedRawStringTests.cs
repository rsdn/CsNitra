using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.11.1 — C# 11.0 interpolated raw string $"""...""" (Cs11.grammar). Positives use
// CreateParser(11) (Cs1+...+Cs11 merged); version-purity negatives use CreateParser(10)
// (Cs1+...+Cs10, no CS11). An interpolated raw string is a raw string with a $ prefix:
//   $"""Hello, {name}!"""
// It is a new Primary alternative (T0.3 append-merge) mirroring the non-interpolated
// RawStringLiteral (Cs11.grammar:19) with a leading $. Roslyn: ScanInterpolatedOrRawStringLiteralTop
// (Lexer_StringLiteral.cs:282-337), ParseInterpolatedOrRawStringToken
// (LanguageParser_InterpolatedString.cs:126-143). See docs/CSharpParserPlan-progressT3.11.1.md.
[TestClass]
public class Cs11InterpolatedRawStringTests
{
    // === POSITIVE (version 11): a simple interpolated raw string. ===

    [TestMethod]
    public void InterpolatedRawString_Simple_Succeeds()
        => AssertParsesV11("class C { string S(string name) => $\"\"\"Hello, {name}!\"\"\"; }");

    // === POSITIVE (version 11): a multi-line interpolated raw string. ===

    [TestMethod]
    public void InterpolatedRawString_MultiLine_Succeeds()
        => AssertParsesV11("class C { string S(string name) => $\"\"\"\n    Hello, {name}!\n    \"\"\"; }");

    // === NEGATIVE (version 10, version-purity): interpolated raw string is not available at v10. ===

    [TestMethod]
    public void InterpolatedRawString_Simple_RejectedAtV10()
        => AssertFailsV10("class C { string S(string name) => $\"\"\"Hello, {name}!\"\"\"; }");

    [TestMethod]
    public void InterpolatedRawString_MultiLine_RejectedAtV10()
        => AssertFailsV10("class C { string S(string name) => $\"\"\"\n    Hello, {name}!\n    \"\"\"; }");

    // === helpers ===

    private static void AssertParsesV11(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(11), input);

    private static void AssertFailsV10(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(10), input);

    private static void AssertParses(CSharpParser parser, string input)
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

    private static void AssertFails(CSharpParser parser, string input)
    {
        var result = parser.Parse(input, "Grammar", out _);
        Assert.IsFalse(
            result.TryGetSuccess(out _, out var end) && end == input.Length
                && parser.Parser.RecoveryDiagnostics.Count == 0,
            $"Expected parse failure or recovery diagnostics for: {input}");
    }
}
