using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.11.4 — C# 11.0 `params Span<T>` / `params ReadOnlySpan<T>` (Cs11.grammar). In C# 11 the
// `params` modifier accepts Span<T> and ReadOnlySpan<T> (not just arrays). Positives use
// CreateParser(11); version-purity negatives use CreateParser(10) (no CS11). `params int[]` still
// works at every version. Roslyn: this is a BINDER change (SourceComplexParameterSymbol.cs:1720-1723,
// IDS_FeatureParamsCollections/FirstClassSpan -> C# 11; :1747 ERR_ParamsMustBeCollection), not a
// parser change (LanguageParser.cs:4938 ParseParameter -> ParseType, 4958). See
// docs/CSharpParserPlan-progressT3.11.4.md.
[TestClass]
public class Cs11ParamsSpanTests
{
    // === POSITIVE (version 11): params Span<int>. ===

    [TestMethod]
    public void ParamsSpanInt_Succeeds()
        => AssertParsesV11("class C { void M(params Span<int> values) { } }");

    [TestMethod]
    public void ParamsSpanString_Succeeds()
        => AssertParsesV11("class C { void M(params Span<string> values) { } }");

    // === POSITIVE (version 11): params ReadOnlySpan<int>. ===

    [TestMethod]
    public void ParamsReadOnlySpanInt_Succeeds()
        => AssertParsesV11("class C { void N(params ReadOnlySpan<int> values) { } }");

    [TestMethod]
    public void ParamsReadOnlySpanChar_Succeeds()
        => AssertParsesV11("class C { void N(params ReadOnlySpan<char> values) { } }");

    // === POSITIVE (version 11): params int[] still works (array form, no version gate). ===

    [TestMethod]
    public void ParamsArrayInt_Succeeds()
        => AssertParsesV11("class C { void O(params int[] values) { } }");

    // === POSITIVE (version 11): params Span<T> alongside other parameters. ===

    [TestMethod]
    public void ParamsSpanInt_WithOtherParams_Succeeds()
        => AssertParsesV11("class C { void M(int a, params Span<int> values) { } }");

    // === POSITIVE (version 10, no-regression): params int[] works pre-11 too. ===

    [TestMethod]
    public void ParamsArrayInt_SucceedsAtV10()
        => AssertParsesV10("class C { void O(params int[] values) { } }");

    // === NEGATIVE (version 10, version-purity): params Span<T> / ReadOnlySpan<T> are C# 11 only. ===

    [TestMethod]
    public void ParamsSpanInt_RejectedAtV10()
        => AssertFailsV10("class C { void M(params Span<int> values) { } }");

    [TestMethod]
    public void ParamsReadOnlySpanInt_RejectedAtV10()
        => AssertFailsV10("class C { void N(params ReadOnlySpan<int> values) { } }");

    // === helpers ===

    private static void AssertParsesV11(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(11), input);

    private static void AssertParsesV10(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(10), input);

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
