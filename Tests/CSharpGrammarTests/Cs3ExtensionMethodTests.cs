using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.2.4 — C# 3.0 extension methods (the `this` parameter modifier). Positives use CreateParser(3)
// (Cs1+Cs2+Cs3 merged); version-purity negatives use CreateParser(2) (Cs1+Cs2, no CS3); the `this`
// expression-primary positive uses CreateParser(1) (Cs1 only — C# 1.0, must stay green).
//
// The KEY new construct is the `this` PARAMETER MODIFIER (`static void M(this int x) { }`), a C# 3.0
// feature. Cs3 re-declares `ParameterModifier` (append, T0.3 merge) to add `"this"`; the Cs1 union is
// `| "ref" | "out" | "params"`, so the merged v3 union is `| "ref" | "out" | "params" | "this"`. The
// `this` in a parameter list is a PARAMETER MODIFIER — a different context from the `this` EXPRESSION
// primary (Primary, Cs1.grammar:679, e.g. `this.M()`), which is UNCHANGED (still parses at v1/v2).
//
// `static` class: `static` is a Cs1 MethodModifier (Cs1.grammar:293, C# 1.0) but NOT a Cs1 ClassModifier
// (static classes are C# 2.0, Roslyn IDS_FeatureStaticClasses -> CSharp2, MessageID.cs:728). Cs2.grammar
// re-declares `ClassModifier` (append) to add `"static"` (CS2). The positive `static class` tests parse
// at v3 because v3 merges Cs1+Cs2+Cs3 (Cs2 supplies `static`). See docs/CSharpParserPlan-progressT3.2.4.md.
//
// The `this` modifier is allowed on ANY parameter at the parse level; the first-parameter-only
// restriction (CS1100) is a BINDER concern (Roslyn CheckParameterModifiers, ParameterHelpers.cs:587).
// See docs/CSharpParserPlan-progressT3.2.4.md for the hand-traces.
[TestClass]
public class Cs3ExtensionMethodTests
{
    // === POSITIVE (version 3): extension methods (the `this` parameter modifier). ===

    [TestMethod]
    public void ExtensionMethod_Simple_Succeeds()
        => AssertParsesV3("static class E { static void M(this int x) { } }");

    [TestMethod]
    public void ExtensionMethod_WithExtraParams_Succeeds()
        => AssertParsesV3("static class E { static int Add(this int x, int y) { return x + y; } }");

    [TestMethod]
    public void ExtensionMethod_ReferenceType_Succeeds()
        => AssertParsesV3("static class E { static void M(this string s) { } }");

    [TestMethod]
    public void ExtensionMethod_ArrayType_Succeeds()
        => AssertParsesV3("static class E { static void M(this int[] xs) { } }");

    // The `this` modifier is allowed on ANY parameter at the parse level (the first-parameter-only
    // restriction, CS1100, is a binder concern). A `this` on a NON-first parameter still parses.
    [TestMethod]
    public void ThisModifier_OnNonFirstParameter_Parses()
        => AssertParsesV3("static class E { static void M(int y, this int x) { } }");

    // === POSITIVE (version 3): Roslyn-derived, adapted to full CS3 declarations. ===

    [TestMethod]
    public void Roslyn_ScopedInParameter_ThisModifier_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs:19226
        // ScopedInParameter1 — "static class C { public static void M(scoped in this int x) { } }".
        // Adapted to CS3: the `scoped in` modifiers are CS13/14 (dropped); the `this` parameter modifier
        // is the CS3 feature under test.
        AssertParsesV3("static class C { static void M(this int x) { } }");
    }

    [TestMethod]
    public void Roslyn_ExtensionMethodWithExtraParam_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Symbol/SymbolDisplay/SymbolDisplayTests.cs:330
        // TestExtensionMethodAsStatic — canonical "public static TSource M<TSource>(this C1<TSource>
        // source, int index) {}". Adapted: a static class with a `this` first parameter + an extra
        // (non-extension) parameter.
        AssertParsesV3("static class C { static void M(this int source, int index) { } }");
    }

    // === POSITIVE (version 3): isolate the `this` modifier (no `static class` involved). ===

    [TestMethod]
    public void ThisParameterModifier_Isolated_Succeeds()
    {
        // `this` as a parameter modifier WITHOUT a static class: at the parse level `this` is a
        // ParameterModifier, so `void M(this int x)` parses at v3 (the static-class/static-method
        // requirement is a binder concern). This isolates the `this` modifier from the `static class`
        // deviation (see the progress doc).
        AssertParsesV3("class E { void M(this int x) { } }");
    }

    // === POSITIVE (version 1 / version 2): the `this` EXPRESSION primary is C# 1.0 — must stay green. ===

    [TestMethod]
    public void ThisExpression_Primary_ParseAtV1_Succeeds()
    {
        // `this.N()` — `this` is a Primary alternative (Cs1.grammar:679), a C# 1.0 expression feature.
        // The Cs3 `ParameterModifier` re-declaration does NOT touch Primary, so this must still parse at
        // v1 (and v2).
        AssertParses(CSharpVersionTestHelper.CreateParser(1), "class C { void M() { this.N(); } }");
    }

    [TestMethod]
    public void ThisExpression_Primary_ParseAtV2_Succeeds()
    {
        // Same `this` expression primary at v2 (Cs1+Cs2) — still C# 1.0, still parses (sanity: the
        // `this` expression primary was not broken by the Cs3 `ParameterModifier` re-declaration).
        AssertParses(CSharpVersionTestHelper.CreateParser(2), "class C { void M() { this.N(); } }");
    }

    // === VERSION-PURITY (version 2, no CS3): the `this` parameter modifier must REJECT at v2. ===

    [TestMethod]
    public void ExtensionMethod_ThisModifier_RejectedAtV2()
    {
        // Version-purity negative: at v2, `static class` IS valid (CS2, from Cs2.grammar), but `this` is
        // NOT a parameter modifier (CS3) -> the method `static void M(this int x)` REJECTS, so the whole
        // input REJECTS at v2 (for the `this` modifier, not the `static class`).
        AssertFailsV2("static class E { static void M(this int x) { } }");
    }

    // === VERSION-PURITY (static class is C# 2.0): parses at v2, rejects at v1. ===

    [TestMethod]
    public void StaticClass_ParsesAtV2_Succeeds()
    {
        // `static class` is C# 2.0 (Roslyn IDS_FeatureStaticClasses -> CSharp2). It parses at v2 (Cs1+Cs2,
        // Cs2 supplies the `static` ClassModifier). No `this` modifier involved.
        AssertParses(CSharpVersionTestHelper.CreateParser(2), "static class E { }");
    }

    [TestMethod]
    public void StaticClass_RejectedAtV1()
    {
        // `static class` is NOT C# 1.0: `static` is not a Cs1 ClassModifier -> REJECTS at v1.
        AssertFails(CSharpVersionTestHelper.CreateParser(1), "static class E { }");
    }

    [TestMethod]
    public void ThisParameterModifier_Isolated_RejectedAtV2()
    {
        // Isolates the `this` modifier (no `static class`): at v2 `this` is not a ParameterModifier, and
        // `this` (a reserved keyword) is not a Type -> `void M(this int x)` REJECTS at v2. This proves
        // the `this` modifier (not the `static class` deviation) is the CS3 feature.
        AssertFailsV2("class E { void M(this int x) { } }");
    }

    // === NEGATIVE (version 3, malformed): must REJECT at v3. ===

    [TestMethod]
    public void ExtensionMethod_ThisWithoutType_Rejected()
    {
        // `M(this)` — `this` is a parameter modifier, but there is no type/name after it: `Parameter`
        // (= ParameterModifier* Type TypeName) requires a Type + TypeName, both missing -> REJECTS.
        // (Roslyn accepts `Goo(this t)` at the parse level and reports CS0246 in the binder; our
        // grammar requires the type+name at the parse level.)
        AssertFailsV3("static class E { static void M(this) { } }");
    }

    [TestMethod]
    public void ExtensionMethod_ThisWithoutName_Rejected()
    {
        // `M(this int)` — `this` (modifier) + `int` (type) but NO parameter name: `Parameter` requires a
        // TypeName after the Type, which is missing -> REJECTS.
        AssertFailsV3("static class E { static void M(this int) { } }");
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
