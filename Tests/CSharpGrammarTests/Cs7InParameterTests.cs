using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.7.2 — C# 7.2 `in` parameter (Cs7.grammar). The `in` PARAMETER modifier — a read-only reference
// parameter: `void M(in int x)` (the `in` modifier BEFORE the parameter's type). Positives use
// CreateParser(7) (Cs1+...+Cs7 merged); version-purity negatives use CreateParser(6) (Cs1+...+Cs6, no
// CS7). See docs/CSharpParserPlan-progressT3.7.2.md for the hand-traces, Roslyn refs, and deviations.
[TestClass]
public class Cs7InParameterTests
{
    // === POSITIVE (version 7): the `in` parameter. ===
    // The `in` is a ParameterModifier (Cs7 re-declaration) BEFORE the parameter's type. Roslyn
    // IsParameterModifierExcludingScoped (LanguageParser.cs:4999: InKeyword) + InArgs_CSharp7
    // (RefReadonlyTests.cs:63).

    [TestMethod]
    public void InParameter_Succeeds()
        => AssertParsesV7("class C { void M(in int x) { } }");

    // === POSITIVE (version 7): the `in` parameter with a body. ===
    // The `in` parameter is used in the method body (`N(x)`). The declaration `void N(int x)` is a plain
    // parameter (no `in`).

    [TestMethod]
    public void InParameter_WithBody_Succeeds()
        => AssertParsesV7("class C { void M(in int x) { N(x); } void N(int x) { } }");

    // === POSITIVE (version 7): the `in` parameter with multiple params. ===
    // The `in` parameter is the first of two; the second is a plain parameter.

    [TestMethod]
    public void InParameter_MultipleParams_Succeeds()
        => AssertParsesV7("class C { void M(in int x, int y) { } }");

    // === POSITIVE (version 1–6): no-regression — the `foreach` `in` keyword still parses. ===
    // Adding `in` to the Cs7 ParameterModifier must NOT break the `in` keyword in a foreach statement
    // (a DIFFERENT context — a statement, not a parameter list; the `in` is a plain literal in the
    // ForEachStatement rule, Cs1.grammar:874). NOTE (Deviations D2/D3): the task's exact form
    // `foreach (var x in y) ... IEnumerable<int> y` does not parse at v2–v6 (`var` is reserved at v2+)
    // and `IEnumerable<int>` is not a Type (no generic type-argument list); this uses a concrete type
    // (`int`) and an array field (`int[] y`), which parse at every version v1–v6.

    [TestMethod]
    public void ForEachInKeyword_ParsesAtV1ToV6()
    {
        for (int version = 1; version <= 6; version++)
            AssertParses(CSharpVersionTestHelper.CreateParser(version), "class C { void M() { foreach (int x in y) { } } int[] y; }");
    }

    // === POSITIVE (version 1): the task's exact `var` foreach form parses at v1. ===
    // NOTE (Deviation D2): `var` is a valid user-defined Type ONLY at v1 (it is reserved at v2+), so
    // `foreach (var x in y)` parses at v1 but NOT at v2–v6. This documents the v1 behavior of the
    // task's exact form (the field is `var y`, a valid v1 field of type `var`).

    [TestMethod]
    public void ForEachVarInKeyword_ParsesAtV1()
        => AssertParses(CSharpVersionTestHelper.CreateParser(1), "class C { void M() { foreach (var x in y) { } } var y; }");

    // === POSITIVE (version 7): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void InParameter_Static_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/RefReadonlyTests.cs:63 (InArgs_CSharp7) —
        // `static void M(in int x)`. Adapted to a full class member.
        AssertParsesV7("class C { static void M(in int x) { } }");
    }

    [TestMethod]
    public void InParameter_MultipleIn_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Emit/CodeGen/CodeGenRefLocalTests.cs:593 (TestInParameter) —
        // `static void TestInParameter(in int z, in int y)`. Adapted: two `in` parameters.
        AssertParsesV7("class C { void M(in int x, in int y) { } }");
    }

    [TestMethod]
    public void InParameter_Generic_Roslyn_Succeeds()
    {
        // Roslyn: the `in` parameter form generalizes to a generic type (`in T x`), where `T` is a type
        // parameter (the `M<T>` is the greedy Cs2 generic TypeName, Cs2.grammar:44). Adapted from the
        // InArgs_CSharp7 form (RefReadonlyTests.cs:63) with a generic type.
        AssertParsesV7("class C { void M<T>(in T x) { } }");
    }

    // === NEGATIVE (version 6, version-purity): the `in` parameter is not available at v6. ===
    // At v6 the `in` ParameterModifier is absent, so `in int x` -> ParameterModifier* matches zero and
    // Type = in FAILS (reserved) -> the Parameter fails -> the method fails.

    [TestMethod]
    public void InParameter_RejectedAtV6()
        => AssertFailsV6("class C { void M(in int x) { } }");

    // === NEGATIVE (version 7, malformed): missing type. ===
    // `void M(in x) { }` — the `in` is a ParameterModifier, then `x` is parsed as the Type (a
    // user-defined type), and `)` is found where the parameter NAME (TypeName) is expected -> the
    // Parameter fails -> the method fails. (In real C# a parameter requires a type; this is a parse
    // error, not just a binder error.)

    [TestMethod]
    public void InParameter_MissingType_Rejected()
        => AssertFailsV7("class C { void M(in x) { } }");

    // === helpers ===

    private static void AssertParsesV7(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(7), input);

    private static void AssertFailsV7(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(7), input);

    private static void AssertFailsV6(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(6), input);

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
