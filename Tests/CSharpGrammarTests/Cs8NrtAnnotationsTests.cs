using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.8.5 — C# 8.0 nullable-reference-type (NRT) annotations `?` / `!` on type names (Cs8.grammar).
// A type name may be followed by `?` (nullable) or `!` (non-null): `string?`, `string!`, `int?`,
// `int!`, `MyClass?`, `MyClass!`. Both are 1-char type-suffix LITERALS in the `Type` postfix loop
// (the SAME `StartsWith` mechanism as the `.` / `*` / `[]` type postfixes; NOT terminals).
//
// Roslyn: `?` -> NullableType (Parser/LanguageParser.cs:7618-7629, 7682); `!` has NO dedicated
// syntax node (no NotNullTypeSyntax anywhere) — it is a one-char type suffix exactly like `?`.
// See docs/CSharpParserPlan-progressT3.8.5.md.
//
// Positives use CreateParser(8) (Cs1+...+Cs8 merged). Version-purity negatives use CreateParser(7)
// (Cs1+...+Cs7, no CS8): the `!` suffix (and, in this grammar, the `?` suffix — never previously
// modeled) are CS8-only, so both REJECT at v7. No pre-existing rule is modified -> no regression.
[TestClass]
public class Cs8NrtAnnotationsTests
{
    // === POSITIVE (version 8): the six NRT annotation forms from the task. ===

    [TestMethod]
    public void Nrt_StringQuestion_Succeeds()
        => AssertParsesV8("class C { string? F; }");

    [TestMethod]
    public void Nrt_StringBang_Succeeds()
        => AssertParsesV8("class C { string! F; }");

    [TestMethod]
    public void Nrt_IntQuestion_Succeeds()
        => AssertParsesV8("class C { int? F; }");

    [TestMethod]
    public void Nrt_IntBang_Succeeds()
        => AssertParsesV8("class C { int! F; }");

    [TestMethod]
    public void Nrt_TypeNameQuestion_Succeeds()
        => AssertParsesV8("class MyClass { } class C { MyClass? F; }");

    [TestMethod]
    public void Nrt_TypeNameBang_Succeeds()
        => AssertParsesV8("class MyClass { } class C { MyClass! F; }");

    // === POSITIVE (version 8): qualified type names carry the annotations too. ===

    [TestMethod]
    public void Nrt_QualifiedQuestion_Succeeds()
        => AssertParsesV8("class C { N.M? F; }");

    [TestMethod]
    public void Nrt_QualifiedBang_Succeeds()
        => AssertParsesV8("class C { N.M! F; }");

    // === POSITIVE (version 8): the suffixes compose in any order with `[]` / `*` (the `Type`
    // postfix loop applies suffixes in INPUT order, minPrecedence not updated in-loop). ===

    [TestMethod]
    public void Nrt_ArrayOfNullable_Succeeds()
        => AssertParsesV8("class C { int?[] F; }");

    [TestMethod]
    public void Nrt_NullableArray_Succeeds()
        => AssertParsesV8("class C { int[]? F; }");

    [TestMethod]
    public void Nrt_StringArrayQuestion_Succeeds()
        => AssertParsesV8("class C { string?[] F; }");

    [TestMethod]
    public void Nrt_PointerToNullable_Succeeds()
        => AssertParsesV8("class C { int?* F; }");

    // === POSITIVE (version 8): the annotations appear in other type contexts (return / parameter /
    // local), not just fields. ===

    [TestMethod]
    public void Nrt_ReturnType_Succeeds()
        => AssertParsesV8("class C { string? M() { return null; } }");

    [TestMethod]
    public void Nrt_Parameter_Succeeds()
        => AssertParsesV8("class C { void M(string? x) { } }");

    [TestMethod]
    public void Nrt_LocalVariable_Succeeds()
        => AssertParsesV8("class C { void M() { string? x = null; } }");

    // === POSITIVE (version 8): DISAMBIGUATION — the NRT suffixes must not shadow the `!` logical
    // NOT (Expression prefix, Cs1.grammar:640) or the `?` ternary (Expression postfix,
    // Cs1.grammar:674). The suffixes are `Type` postfixes (tried only while parsing a Type); the
    // NOT / ternary are `Expression` operators (tried only while parsing an Expression). ===

    [TestMethod]
    public void Disambig_LogicalNotExpression_Succeeds()
        => AssertParsesV8("class C { bool x; void M() { bool b = !x; } }");

    [TestMethod]
    public void Disambig_TernaryExpression_Succeeds()
        => AssertParsesV8("class C { bool a; void M() { int b = a ? 1 : 2; } }");

    // === POSITIVE (version 7, no-regression): the `!` NOT and `?` ternary are C# 1.0 EXPRESSION
    // operators and are UNCHANGED by the CS8 `Type` suffixes -> they still parse at v7. ===

    [TestMethod]
    public void Disambig_LogicalNot_ParsesAtV7()
        => AssertParsesV7("class C { bool x; void M() { bool b = !x; } }");

    [TestMethod]
    public void Disambig_Ternary_ParsesAtV7()
        => AssertParsesV7("class C { bool a; void M() { int b = a ? 1 : 2; } }");

    // === NEGATIVE (version 7, version-purity): the `!` suffix is CS8-only. At v7 the Cs8 `Type`
    // re-declaration is absent, so no `Type` postfix consumes the trailing `!`; the field
    // declaration sees `!` where `;` is expected -> REJECTS. ===

    [TestMethod]
    public void Nrt_StringBang_RejectedAtV7()
        => AssertFailsV7("class C { string! F; }");

    [TestMethod]
    public void Nrt_IntBang_RejectedAtV7()
        => AssertFailsV7("class C { int! F; }");

    [TestMethod]
    public void Nrt_TypeNameBang_RejectedAtV7()
        => AssertFailsV7("class MyClass { } class C { MyClass! F; }");

    // === NEGATIVE (version 7, version-purity): the `?` suffix is likewise CS8-only in this grammar
    // (it was never previously modeled — Cs1.grammar:503-504; Cs1TypeTests.Invalid_NrtAnnotation_Fails
    // / Invalid_NullableValueType_Fails assert `?` fails at v1). So `?` also REJECTS at v7. ===

    [TestMethod]
    public void Nrt_StringQuestion_RejectedAtV7()
        => AssertFailsV7("class C { string? F; }");

    [TestMethod]
    public void Nrt_IntQuestion_RejectedAtV7()
        => AssertFailsV7("class C { int? F; }");

    // === helpers ===

    private static void AssertParsesV8(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(8), input);

    private static void AssertFailsV8(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(8), input);

    private static void AssertParsesV7(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(7), input);

    private static void AssertFailsV7(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(7), input);

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
