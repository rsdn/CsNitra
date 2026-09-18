using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.14.1 — C# 14.0 `extension` container. The `extension` container is a TYPE-LEVEL declaration
// that holds extension properties/indexers/events/operators/fields. It has NO name, an OPTIONAL
// type-parameter list, a REQUIRED parameter list (at least one — Roslyn requireOneElement:true),
// optional `where` constraints, and a body (`{ members* }` or `;`):
//   extension (this int x)
//   {
//       public static string ToHex() { return x.ToString("X"); }
//   }
//
// Roslyn: ParseMainTypeDeclaration (LanguageParser.cs:1789-1928) with name=null; IsExtensionContainerStart
// (3396-3401); a C# 14.0 feature (MessageID.IDS_FeatureExtensions, C# 14 block).
//
// Cs14.grammar models it by re-declaring TypeDeclaration (append, T0.3 merge, like the Cs9 record) to
// add ExtensionDeclaration = "extension" TypeParameterList? ExtensionParameterList ConstraintClause*
// ExtensionBody. ExtensionParameterList = "(" Parameter+ ")" (reuses Cs1 Parameter; the `this` modifier
// is the Cs3 feature). ExtensionBody = ClassBody | ";" (reuses Cs1 ClassBody). All Cs14-only.
//
// Positives use CreateParser(14); version-purity negatives use CreateParser(13) (Cs1..Cs13, no CS14):
// at v13 the ExtensionDeclaration alternative is ABSENT, so `extension ( ... )` REJECTS.
[TestClass]
public class Cs14ExtensionContainerTests
{
    // === POSITIVE (version 14): the `extension` container. ===

    [TestMethod]
    public void ExtensionContainer_Simple_Succeeds()
        => AssertParsesV14("extension (this int x) { public static string ToHex() { return x.ToString(\"X\"); } }");

    [TestMethod]
    public void ExtensionContainer_EmptyBody_Succeeds()
        => AssertParsesV14("extension (this int x) { }");

    [TestMethod]
    public void ExtensionContainer_SemicolonBody_Succeeds()
        => AssertParsesV14("extension (this int x) ;");

    [TestMethod]
    public void ExtensionContainer_TypeParameters_Succeeds()
        => AssertParsesV14("extension<T> (this T x) { }");

    [TestMethod]
    public void ExtensionContainer_Constraints_Succeeds()
        => AssertParsesV14("extension<T> (this T x) where T : struct { }");

    [TestMethod]
    public void ExtensionContainer_MultipleMembers_Succeeds()
        => AssertParsesV14("extension (this int x) { public static string ToHex() { return x.ToString(\"X\"); } public static bool IsPositive() { return x > 0; } }");

    [TestMethod]
    public void ExtensionContainer_MultipleParameters_Succeeds()
        => AssertParsesV14("extension (this int x, int y) { }");

    // === VERSION-PURITY (version 13, no CS14): the `extension` container must REJECT. ===
    // At v13 the ExtensionDeclaration alternative is ABSENT, so `extension ( ... )` matches no
    // TypeDeclaration alternative and REJECTS.

    [TestMethod]
    public void ExtensionContainer_Simple_RejectedAtV13()
        => AssertFailsV13("extension (this int x) { }");

    [TestMethod]
    public void ExtensionContainer_TypeParameters_RejectedAtV13()
        => AssertFailsV13("extension<T> (this T x) { }");

    [TestMethod]
    public void ExtensionContainer_SemicolonBody_RejectedAtV13()
        => AssertFailsV13("extension (this int x) ;");

    // === NEGATIVE (version 14, malformed): must REJECT at v14. ===

    [TestMethod]
    public void ExtensionContainer_EmptyParameterList_Rejected()
    {
        // `extension () { }` — the parameter list REQUIRES at least one element (Roslyn
        // requireOneElement:true); ExtensionParameterList = "(" Parameter+ ")" fails on "()", so the
        // ExtensionDeclaration (and thus the TypeDeclaration) fails.
        AssertFailsV14("extension () { }");
    }

    // === helpers ===

    private static void AssertParsesV14(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(14), input);

    private static void AssertFailsV13(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(13), input);

    private static void AssertFailsV14(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(14), input);

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
