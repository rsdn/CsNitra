using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.13.1 — C# 13.0 `field` keyword. The `field` keyword refers to the backing field of an
// auto-property and can be used in the getter/setter body:
//   class MyClass {
//       public int Value {
//           get => field;
//           set => field = value;
//       }
//   }
//
// Roslyn: `field` is a CONTEXTUAL keyword parsed as a FieldExpression in primary-expression position
// within a property accessor body (LanguageParser.cs:12009-12012, IsCurrentTokenFieldInKeywordContext
// 6097-6102); SyntaxKind.FieldKeyword (SyntaxKind.cs:349); a C# 13.0 feature
// (MessageID.IDS_FeatureFieldKeyword).
//
// Cs13.grammar models it three ways: (1) a FieldExpr primary (`field` as a primary expression),
// (2) a `field` reservation at v13 (so FieldExpr is the sole Primary match — no tie with
// IdentifierName), and (3) expression-bodied accessors (`get => Expr ;` / `set => Expr ;`) via a
// re-declared `Property` alternative (ExpressionBodiedAccessorList). All three are Cs13-only.
//
// Positives use CreateParser(13); version-purity negatives use CreateParser(12) (Cs1..Cs12, no CS13):
// at v12 the `=>` accessor form is absent, so `get => field;` / `set => field = value;` REJECT.
[TestClass]
public class Cs13FieldKeywordTests
{
    // === POSITIVE (version 13): the `field` keyword in accessor bodies. ===

    [TestMethod]
    public void FieldKeyword_GetterSetter_Succeeds()
        => AssertParsesV13("class MyClass { public int Value { get => field; set => field = value; } }");

    [TestMethod]
    public void FieldKeyword_GetterOnly_Succeeds()
        => AssertParsesV13("class C { public int X { get => field; } }");

    [TestMethod]
    public void FieldKeyword_SetterAssignment_Succeeds()
        => AssertParsesV13("class C { public int X { get => field; set => field = value; } }");

    [TestMethod]
    public void FieldKeyword_Struct_Succeeds()
        => AssertParsesV13("struct S { public int X { get => field; set => field = value; } }");

    [TestMethod]
    public void FieldKeyword_MultipleProperties_Succeeds()
        => AssertParsesV13("class C { public int A { get => field; } public int B { get => field; set => field = value; } }");

    [TestMethod]
    public void FieldKeyword_FieldExpressionInComplexGetter_Succeeds()
    {
        // `field` as a primary expression inside a larger expression (an arithmetic expression).
        AssertParsesV13("class C { public int X { get => field + 1; set => field = value; } }");
    }

    // === VERSION-PURITY (version 12, no CS13): the `field` keyword in an accessor body must REJECT. ===
    // At v12 the `=>` (expression-bodied) accessor form is absent, so the whole accessor list fails.

    [TestMethod]
    public void FieldKeyword_GetterSetter_RejectedAtV12()
        => AssertFailsV12("class MyClass { public int Value { get => field; set => field = value; } }");

    [TestMethod]
    public void FieldKeyword_GetterOnly_RejectedAtV12()
        => AssertFailsV12("class C { public int X { get => field; } }");

    [TestMethod]
    public void FieldKeyword_SetterAssignment_RejectedAtV12()
        => AssertFailsV12("class C { public int X { get => field; set => field = value; } }");

    // === REGRESSION (version 12): pre-existing property forms must stay green (no CS13 leak). ===

    [TestMethod]
    public void BlockBodyProperty_StillParsesAtV12_Succeeds()
    {
        // The Cs1 concrete property (all BLOCK accessors) is a SEPARATE alternative of the merged
        // `Property`; it must still match at v12 (the Cs13 `=>` alternative must not break it).
        AssertParsesV12("class C { int _x; int X { get { return _x; } set { _x = value; } } }");
    }

    [TestMethod]
    public void AutoProperty_StillParsesAtV12_Succeeds()
    {
        // The Cs3 auto-property (`get; set;`) is a SEPARATE alternative; it must still match at v12.
        AssertParsesV12("class C { public int X { get; set; } }");
    }

    // === NEGATIVE (version 13, malformed): must REJECT at v13. ===

    [TestMethod]
    public void FieldKeyword_MissingSemicolon_Rejected()
    {
        // `get => field }` — a missing `;` after the expression body: no accessor form matches
        // (`ExprGetterOnly` requires a `;`), so the property fails.
        AssertFailsV13("class C { public int X { get => field } }");
    }

    // === helpers ===

    private static void AssertParsesV13(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(13), input);

    private static void AssertParsesV12(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(12), input);

    private static void AssertFailsV13(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(13), input);

    private static void AssertFailsV12(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(12), input);

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
