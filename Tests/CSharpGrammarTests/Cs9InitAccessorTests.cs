using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.9.3 — C# 9.0 `init` accessor (init-only setter, Cs9.grammar). Positives use
// CreateParser(9) (Cs1+...+Cs9 merged); version-purity negatives use CreateParser(8) (Cs1+...+Cs8, no
// CS9). The `init` accessor is a first-class accessor name (like `get`/`set`) that can appear in a
// property's accessor list: `public int X { get; init; }`, `public int Y { get; set; init; }`. It is
// modeled by re-declaring AutoPropertyAccessorList (Cs3.grammar:182-186, append-merge) with two new
// auto (semicolon) shapes (AutoGetInit, AutoGetSetInit). See docs/CSharpParserPlan-progressT3.9.3.md.
//
// Roslyn: ParseAccessorDeclaration (LanguageParser.cs:4632-4729) + GetAccessorKind (4737-4748,
// InitKeyword => InitAccessorDeclaration, 4743); `init` is a contextual keyword (SyntaxKindFacts.cs:1419-1420).
[TestClass]
public class Cs9InitAccessorTests
{
    // === POSITIVE (version 9): a property with `get; init;` (init-only setter, no set). ===
    // Roslyn: AccessorList with a GetAccessorDeclaration (`get;`) + an InitAccessorDeclaration (`init;`).

    [TestMethod]
    public void InitAccessor_GetInit_Succeeds()
        => AssertParsesV9("class C { public int X { get; init; } }");

    // === POSITIVE (version 9): a property with `get; set; init;`. ===
    // All three accessors are auto (semicolon) forms; the new AutoGetSetInit shape matches.

    [TestMethod]
    public void InitAccessor_GetSetInit_Succeeds()
        => AssertParsesV9("class C { public int Y { get; set; init; } }");

    // === POSITIVE (version 9): `init` accessor in a struct. ===

    [TestMethod]
    public void InitAccessor_Struct_Succeeds()
        => AssertParsesV9("struct S { public int X { get; init; } }");

    // === POSITIVE (version 9): `init` accessor with a property access modifier. ===
    // `private` is a PropertyModifier (Cs1.grammar:220); the accessor list is `get; init;`.

    [TestMethod]
    public void InitAccessor_Private_Succeeds()
        => AssertParsesV9("class C { private int X { get; init; } }");

    // === POSITIVE (version 9): regression — a plain `get; set;` (no `init`) still parses at v9. ===
    // Confirms the Cs3 auto-property path (AutoPropertyAccessorList) is not broken by the new shapes.

    [TestMethod]
    public void Regression_GetSet_NoInit_Succeeds()
        => AssertParsesV9("class C { public int X { get; set; } }");

    // === VERSION-PURITY (version 8, no CS9): `get; init;` must REJECT at v8. ===
    // At v8 the AutoGetInit shape is ABSENT (Cs9-only); `get; init;` leaves `init;` unconsumed -> REJECTS.

    [TestMethod]
    public void InitAccessor_GetInit_RejectedAtV8()
        => AssertFailsV8("class C { public int X { get; init; } }");

    // === VERSION-PURITY (version 8, no CS9): `get; set; init;` must REJECT at v8. ===

    [TestMethod]
    public void InitAccessor_GetSetInit_RejectedAtV8()
        => AssertFailsV8("class C { public int Y { get; set; init; } }");

    // === NEGATIVE (version 9, malformed): `init` without a `get` accessor is invalid. ===
    // C# properties require a `get` (binder CS0858); every AutoPropertyAccessorList shape is get-first,
    // so a bare `init;` leaves the accessor list unmatched -> REJECTS.

    [TestMethod]
    public void InitAccessor_WithoutGet_Rejected()
        => AssertFailsV9("class C { public int X { init; } }");

    // === helpers ===

    private static void AssertParsesV9(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(9), input);

    private static void AssertFailsV9(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(9), input);

    private static void AssertFailsV8(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(8), input);

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
