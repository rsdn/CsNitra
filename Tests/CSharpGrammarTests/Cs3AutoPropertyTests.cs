using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.2.3 — C# 3.0 auto-properties (class/struct only). Positives use CreateParser(3) (Cs1+Cs2+Cs3
// merged); version-purity negatives use CreateParser(2) (Cs1+Cs2, no CS3); the interface auto-accessor
// positive uses CreateParser(1) (Cs1 only — C# 1.0, must stay green).
//
// The Cs1 concrete class/struct property (Cs1.grammar:188) requires AccessorDeclaration+ =
// AccessorName Block (every accessor a BLOCK body), so `class C { int P { get; set; } }` REJECTS at
// Cs1. Cs3 re-declares `Property` with an alternative that REQUIRES at least one AUTO (semicolon)
// accessor (AutoPropertyAccessorList) + an optional `= Expression` initializer. The two alternatives
// are mutually exclusive (all-block vs >= one auto) — no equal-length tie. The interface path
// (InterfaceProperty, Cs1.grammar:209) is a SEPARATE rule and is unchanged (C# 1.0).
//
// Forms (Roslyn ParseAccessorDeclaration LanguageParser.cs:4632-4729: block / semicolon / arrow(CS7)):
//   { get; set; }            full auto-property
//   { get; }                 read-only auto-property
//   { get; set; } = 5;       auto-property with initializer
//   { get { ... } set; }     auto-setter (block getter + auto setter)
//   { get; set { ... } }     auto-getter (auto getter + block setter)
// See docs/CSharpParserPlan-progressT3.2.3.md for the hand-traces and the CS6-initializer deviation.
[TestClass]
public class Cs3AutoPropertyTests
{
    // === POSITIVE (version 3): auto-properties. ===

    [TestMethod]
    public void AutoProperty_GetSet_Succeeds()
        => AssertParsesV3("class C { public int X { get; set; } }");

    [TestMethod]
    public void AutoProperty_GetOnly_Succeeds()
        => AssertParsesV3("class C { public int X { get; } }");

    [TestMethod]
    public void AutoProperty_WithInitializer_Succeeds()
        => AssertParsesV3("class C { public int X { get; set; } = 5; }");

    [TestMethod]
    public void AutoSetter_BlockGetter_Succeeds()
    {
        // "auto-setter": a block getter + an auto (semicolon) setter. The getter body uses the
        // private backing field `_x`; `value` (the implicit setter parameter) is a true identifier.
        AssertParsesV3("class C { private int _x; public int X { get { return _x; } set; } }");
    }

    [TestMethod]
    public void AutoGetter_BlockSetter_Succeeds()
    {
        // "auto-getter": an auto (semicolon) getter + a block setter that assigns to `_x`.
        AssertParsesV3("class C { private int _x; public int X { get; set { _x = value; } } }");
    }

    [TestMethod]
    public void AutoProperty_Multiple_Succeeds()
        => AssertParsesV3("class C { public int X { get; set; } public int Y { get; } = 10; }");

    [TestMethod]
    public void AutoProperty_Struct_Succeeds()
        => AssertParsesV3("struct S { public int X { get; set; } }");

    [TestMethod]
    public void AutoProperty_ReadOnlyWithInitializer_Succeeds()
        => AssertParsesV3("class C { public int X { get; } = 10; }");

    [TestMethod]
    public void AutoProperty_ExpressionInitializer_Succeeds()
        => AssertParsesV3("class C { public int X { get; set; } = 5 + 3; }");

    [TestMethod]
    public void BlockBodyProperty_StillParsesAtV3_Succeeds()
    {
        // The Cs1 concrete property (all BLOCK accessors) is a SEPARATE alternative of the merged
        // `Property`; it must still match at v3 (mutual exclusivity with the Cs3 auto alternative).
        AssertParsesV3("class C { int X { get { return _x; } set { _x = value; } } int _x; }");
    }

    [TestMethod]
    public void MixedBlockAndAutoProperty_Succeeds()
    {
        // A class holding BOTH a block-body property (Cs1 alternative) and an auto-property (Cs3
        // alternative) — the two alternatives coexist (no equal-length tie).
        AssertParsesV3("class C { int _x; int A { get { return _x; } set { _x = value; } } int B { get; set; } }");
    }

    // === POSITIVE (version 3): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void Roslyn_TestClassProperty_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs:4158
        // TestClassProperty — "class a { b c { get; set; } }" (full auto-property).
        AssertParsesV3("class C { int X { get; set; } }");
    }

    [TestMethod]
    public void Roslyn_TestClassAutoPropertyWithInitializer_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs:4448
        // TestClassAutoPropertyWithInitializer — "class a { b c { get; set; } = d; }".
        AssertParsesV3("class C { int X { get; set; } = 5; }");
    }

    // === POSITIVE (version 1 / version 2): interface auto-accessors are C# 1.0 — must stay green. ===

    [TestMethod]
    public void InterfaceProperty_AutoAccessors_ParseAtV1_Succeeds()
    {
        // The INTERFACE property uses `;` accessors (InterfaceProperty, Cs1.grammar:209) — a C# 1.0
        // feature. It is a SEPARATE rule from the class/struct `Property`, so the Cs3 auto-property
        // re-declaration does not affect it. Verify it still parses at v1.
        AssertParses(CSharpVersionTestHelper.CreateParser(1), "interface I { int P { get; set; } }");
    }

    [TestMethod]
    public void InterfaceProperty_AutoAccessors_ParseAtV2_Succeeds()
    {
        // Same interface form at v2 (Cs1+Cs2) — still C# 1.0, still parses (sanity: the interface
        // path was not broken by the Cs3 `Property` re-declaration).
        AssertParses(CSharpVersionTestHelper.CreateParser(2), "interface I { int P { get; set; } }");
    }

    // === VERSION-PURITY (version 2, no CS3): class auto-properties must REJECT at v2. ===

    [TestMethod]
    public void AutoProperty_GetSet_RejectedAtV2()
        => AssertFailsV2("class C { int P { get; set; } }");

    [TestMethod]
    public void AutoProperty_GetOnly_RejectedAtV2()
        => AssertFailsV2("class C { int P { get; } }");

    [TestMethod]
    public void AutoProperty_WithInitializer_RejectedAtV2()
        => AssertFailsV2("class C { int P { get; set; } = 5; }");

    // === NEGATIVE (version 3, malformed): must REJECT at v3. ===

    [TestMethod]
    public void AutoProperty_MissingAccessorBody_Rejected()
    {
        // `get set;` — a missing `;`/`{` after the first accessor: no accessor form (block,
        // semicolon, or the Cs1 block) matches `get set`.
        AssertFailsV3("class C { int P { get set; } }");
    }

    [TestMethod]
    public void AutoProperty_MissingInitializerValue_Rejected()
    {
        // `= }` — a missing initializer value: the `("= Expression)?` backtracks (no expression after
        // `=`), so the property matches `int P { get; set; }` and the leftover `= }` is not a valid
        // member -> the class body fails.
        AssertFailsV3("class C { int P { get; set; } = }");
    }

    [TestMethod]
    public void AutoProperty_SetOutsideAccessorList_Rejected()
    {
        // `int P { get; } set;` — the `set;` sits OUTSIDE the accessor list (after the property's
        // closing `}`). The property matches `int P { get; }`; the leftover `set;` is not a valid
        // member -> the class body fails.
        AssertFailsV3("class C { int P { get; } set; }");
    }

    // === helpers ===

    private static void AssertParsesV3(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(3), input);

    private static void AssertFailsV3(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(3), input);

    private static void AssertFailsV2(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(2), input);

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
