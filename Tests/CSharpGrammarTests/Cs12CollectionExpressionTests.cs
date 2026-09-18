using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.12.2 — C# 12.0 collection expressions (Cs12.grammar). A collection expression is a PRIMARY
// expression that creates a collection from its elements: `var list = [1, 2, 3];`, `var empty = [];`.
// Positives use CreateParser(12) (Cs1+...+Cs12 merged); version-purity negatives use CreateParser(11)
// (Cs1+...+Cs11, no CS12). See docs/CSharpParserPlan-progressT3.12.2.md.
//
// Each input is wrapped in a version-neutral method-body context (`class C { void M() { ... } }`) so the
// ONLY CS12 feature is the collection expression itself: at v11 the `[...]` initializer matches no Primary
// alternative and REJECTS, while at v12 the Cs12 CollectionExpr alternative (Cs12.grammar) accepts it.
[TestClass]
public class Cs12CollectionExpressionTests
{
    // === POSITIVE (version 12): a simple collection expression with several elements. ===
    // Roslyn ParseCollectionExpression (LanguageParser.cs:13264-13291).

    [TestMethod]
    public void CollectionExpression_Simple_Succeeds()
        => AssertParsesV12("class C { void M() { var list = [1, 2, 3]; } }");

    // === POSITIVE (version 12): an empty collection expression. ===
    // Roslyn requireOneElement:false (LanguageParser.cs:13275) — an empty list is allowed.

    [TestMethod]
    public void CollectionExpression_Empty_Succeeds()
        => AssertParsesV12("class C { void M() { var empty = []; } }");

    // === POSITIVE (version 12): a collection expression with a single element. ===

    [TestMethod]
    public void CollectionExpression_SingleElement_Succeeds()
        => AssertParsesV12("class C { void M() { var x = [1]; } }");

    // === POSITIVE (version 12): identifier elements. ===

    [TestMethod]
    public void CollectionExpression_IdentifierElements_Succeeds()
        => AssertParsesV12("class C { void M() { var x = [a, b, c]; } }");

    // === POSITIVE (version 12): an invocation as a single element. ===

    [TestMethod]
    public void CollectionExpression_InvocationElement_Succeeds()
        => AssertParsesV12("class C { void M() { var x = [f(1)]; } }");

    // === POSITIVE (version 12, regression): an array LITERAL still parses at v12. ===
    // The `[` of the array type (`int[]`) and the `{ ... }` initializer are NOT a collection
    // expression (the collection expression is a primary starting with `[`); the two are distinct.

    [TestMethod]
    public void ArrayLiteral_StillSucceedsAtV12()
        => AssertParsesV12("class C { void M() { int[] a = new int[] { 1, 2, 3 }; } }");

    // === POSITIVE (version 12, regression): a postfix INDEXER still parses at v12. ===
    // The `[` of `a[1, 2]` is a PostfixOp Indexer (Cs1.grammar:776), not a collection expression: it
    // requires a preceding primary (`a`), so it never competes with a primary-position `[`.

    [TestMethod]
    public void Indexer_StillSucceedsAtV12()
        => AssertParsesV12("class C { void M() { int[] a; a[1, 2] = 0; } }");

    // === NEGATIVE (version 11, version-purity): a simple collection expression is not available at v11. ===
    // At v11 the Cs12 CollectionExpr alternative is ABSENT; the `[1, 2, 3]` initializer matches no Primary
    // alternative -> the var declaration fails -> the declaration is unconsumed -> REJECTS. At v12 it PARSES.

    [TestMethod]
    public void CollectionExpression_Simple_RejectedAtV11()
        => AssertFailsV11("class C { void M() { var list = [1, 2, 3]; } }");

    // === NEGATIVE (version 11, version-purity): an empty collection expression at v11. ===

    [TestMethod]
    public void CollectionExpression_Empty_RejectedAtV11()
        => AssertFailsV11("class C { void M() { var empty = []; } }");

    // === NEGATIVE (version 11, version-purity): a single-element collection expression at v11. ===

    [TestMethod]
    public void CollectionExpression_SingleElement_RejectedAtV11()
        => AssertFailsV11("class C { void M() { var x = [1]; } }");

    // === NEGATIVE (version 12, malformed): missing the closing bracket. ===

    [TestMethod]
    public void CollectionExpression_MissingCloseBracket_Rejected()
        => AssertFailsV12("class C { void M() { var x = [1, 2; } }");

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
