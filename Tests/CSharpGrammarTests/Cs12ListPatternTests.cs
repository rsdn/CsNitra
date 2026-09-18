using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.12.3 — C# 12.0 list patterns (Cs12.grammar). A list pattern is a PATTERN that matches a list:
// `if (list is [1, 2, 3]) { }`, `if (list is [var first, ..]) { }`, `if (list is [1, .., 3]) { }`.
// Positives use CreateParser(12) (Cs1+...+Cs12 merged); version-purity negatives use CreateParser(11)
// (Cs1+...+Cs11, no CS12). See docs/CSharpParserPlan-progressT3.12.3.md.
//
// Each input is wrapped in a version-neutral method-body context (`class C { void M() { ... } }`) so the
// ONLY CS12 feature is the list pattern itself: at v11 the `[...]` pattern matches no Pattern alternative
// (the Cs12 ListPattern/SlicePattern are absent and the CollectionExpr is absent from the ConstantPattern)
// and REJECTS, while at v12 the Cs12 ListPattern alternative (Cs12.grammar) accepts it.
[TestClass]
public class Cs12ListPatternTests
{
    // === POSITIVE (version 12): a simple all-constant list pattern. ===
    // Roslyn ParseListPattern (LanguageParser_Patterns.cs:660-678).

    [TestMethod]
    public void ListPattern_Simple_Succeeds()
        => AssertParsesV12("class C { void M() { if (list is [1, 2, 3]) { } } }");

    // === POSITIVE (version 12): a list pattern with a var element and a trailing slice. ===
    // The "var first" element is a VarPattern (Cs7.grammar:133) and the ".." element is the Cs12
    // SlicePattern. The ConstantPattern/CollectionExpr CANNOT match this (a "var" is not an Expression),
    // so this is parsed as a genuine ListPattern.

    [TestMethod]
    public void ListPattern_VarFirst_Succeeds()
        => AssertParsesV12("class C { void M() { if (list is [var first, ..]) { } } }");

    // === POSITIVE (version 12): a list pattern with a slice in the middle. ===
    // The ".." element is the Cs12 SlicePattern; the "1" / "3" elements are ConstantPatterns. The
    // ConstantPattern/CollectionExpr CANNOT match this (the ".." is not an Expression), so this is a
    // genuine ListPattern.

    [TestMethod]
    public void ListPattern_SliceMiddle_Succeeds()
        => AssertParsesV12("class C { void M() { if (list is [1, .., 3]) { } } }");

    // === POSITIVE (version 12): an empty list pattern. ===
    // Roslyn requireOneElement:false (LanguageParser_Patterns.cs:670) — an empty list is allowed.

    [TestMethod]
    public void ListPattern_Empty_Succeeds()
        => AssertParsesV12("class C { void M() { if (list is []) { } } }");

    // === POSITIVE (version 12, regression): a C# 7.0 type `is` pattern still parses at v12. ===
    // The list pattern is a NEW Pattern alternative; it must not disturb the existing type pattern
    // (TypeIs, Cs1.grammar:706 / TypeIsPattern, Cs7.grammar:108).

    [TestMethod]
    public void TypePattern_IsInt_StillSucceedsAtV12()
        => AssertParsesV12("class C { void M() { if (x is int) { } } }");

    // === POSITIVE (version 12, regression): the Cs12 collection expression still parses at v12. ===
    // The collection expression (T3.12.2) is a PRIMARY EXPRESSION (in `Primary`), the list pattern is a
    // PATTERN (in `Pattern`) — different syntactic contexts, so the two do not conflict.

    [TestMethod]
    public void CollectionExpression_StillSucceedsAtV12()
        => AssertParsesV12("class C { void M() { var list = [1, 2, 3]; } }");

    // === NEGATIVE (version 11, version-purity): a simple list pattern is not available at v11. ===
    // At v11 the Cs12 ListPattern/SlicePattern are ABSENT and the CollectionExpr is ABSENT from the
    // ConstantPattern (Expression), so `[1, 2, 3]` matches no Pattern alternative -> the `is` fails ->
    // REJECTS. At v12 it PARSES.

    [TestMethod]
    public void ListPattern_Simple_RejectedAtV11()
        => AssertFailsV11("class C { void M() { if (list is [1, 2, 3]) { } } }");

    // === NEGATIVE (version 11, version-purity): a var + slice list pattern at v11. ===

    [TestMethod]
    public void ListPattern_VarFirst_RejectedAtV11()
        => AssertFailsV11("class C { void M() { if (list is [var first, ..]) { } } }");

    // === NEGATIVE (version 11, version-purity): a slice-middle list pattern at v11. ===

    [TestMethod]
    public void ListPattern_SliceMiddle_RejectedAtV11()
        => AssertFailsV11("class C { void M() { if (list is [1, .., 3]) { } } }");

    // === NEGATIVE (version 11, version-purity): an empty list pattern at v11. ===

    [TestMethod]
    public void ListPattern_Empty_RejectedAtV11()
        => AssertFailsV11("class C { void M() { if (list is []) { } } }");

    // === NEGATIVE (version 12, malformed): missing the closing bracket. ===

    [TestMethod]
    public void ListPattern_MissingCloseBracket_Rejected()
        => AssertFailsV12("class C { void M() { if (list is [1, 2 { } } }");

    // === helpers ===

    private static void AssertParsesV12(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(12), input);

    private static void AssertFailsV12(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(12), input);

    private static void AssertFailsV11(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(11), input);

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
