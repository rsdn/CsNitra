using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.7.3 — C# 7.2 `ref struct` + `readonly struct` (Cs7.grammar). The `ref`/`readonly` STRUCT modifier —
// a modifier BEFORE the `struct` keyword: `ref struct S { }` / `readonly struct S { }`. Positives use
// CreateParser(7) (Cs1+...+Cs7 merged); version-purity negatives use CreateParser(6) (Cs1+...+Cs6, no
// CS7). See docs/CSharpParserPlan-progressT3.7.3.md for the hand-traces, Roslyn refs, and deviations.
[TestClass]
public class Cs7RefStructTests
{
    // === POSITIVE (version 7): the `ref struct`. ===
    // The `ref` is a StructModifier (Cs7 re-declaration) BEFORE the `struct` keyword. Roslyn
    // DeclarationTreeBuilder.cs:804 (RefKeyword + Struct -> IDS_FeatureRefStructs) + RefStructs.cs:28.

    [TestMethod]
    public void RefStruct_Succeeds()
        => AssertParsesV7("ref struct S { }");

    // === POSITIVE (version 7): the `ref struct` with a member. ===
    // A method inside the ref struct body.

    [TestMethod]
    public void RefStruct_WithMember_Succeeds()
        => AssertParsesV7("ref struct S { int M() { return 5; } }");

    // === POSITIVE (version 7): the `readonly struct`. ===
    // The `readonly` is a StructModifier (Cs7 re-declaration) BEFORE the `struct` keyword. Roslyn
    // DeclarationTreeBuilder.cs:800 (ReadOnlyKeyword + Struct -> IDS_FeatureReadOnlyStructs) +
    // ReadOnlyStructs.cs:27.

    [TestMethod]
    public void ReadonlyStruct_Succeeds()
        => AssertParsesV7("readonly struct S { }");

    // === POSITIVE (version 7): the `readonly struct` with a member. ===
    // A method inside the readonly struct body.

    [TestMethod]
    public void ReadonlyStruct_WithMember_Succeeds()
        => AssertParsesV7("readonly struct S { int M() { return 5; } }");

    // === POSITIVE (version 1–6): no-regression — the `readonly` FIELD still parses. ===
    // Adding `readonly` to the Cs7 StructModifier must NOT break the `readonly` field (a DIFFERENT rule —
    // FieldModifier, Cs1.grammar:162). The `readonly` struct modifier and the `readonly` field modifier are
    // disambiguated by what follows: `struct` for a struct declaration, a type for a field.

    [TestMethod]
    public void ReadonlyField_ParsesAtV1ToV6()
    {
        for (int version = 1; version <= 6; version++)
            AssertParses(CSharpVersionTestHelper.CreateParser(version), "class C { readonly int x; }");
    }

    // === POSITIVE (version 7): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void RefStruct_Public_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/RefStructs.cs:35 (RefStructSimple) —
        // `public ref struct S2{}`. Adapted to a top-level declaration.
        AssertParsesV7("public ref struct S { }");
    }

    [TestMethod]
    public void ReadonlyStruct_Public_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ReadOnlyStructs.cs:34 (ReadOnlyStructSimple) —
        // `public readonly struct S2{}`. Adapted to a top-level declaration.
        AssertParsesV7("public readonly struct S { }");
    }

    [TestMethod]
    public void ReadonlyRefStruct_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ReadOnlyStructs.cs:143 (ReadOnlyRefStruct) —
        // `readonly ref struct S1{}` (BOTH modifiers combined, any order). Adapted to a top-level
        // declaration.
        AssertParsesV7("readonly ref struct S { }");
    }

    [TestMethod]
    public void ReadonlyStruct_MemberOrder_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ReadOnlyStructs.cs:36 (ReadOnlyStructSimple) —
        // `readonly public struct S3{}` (the `readonly` BEFORE the access modifier — a flexible modifier
        // ORDER). Roslyn's ParseModifiers is a generic any-order loop, so the parser accepts either order;
        // ordering/legality is a binder concern.
        AssertParsesV7("readonly public struct S { }");
    }

    // === NEGATIVE (version 6, version-purity): `ref struct` / `readonly struct` are not available at v6. ===
    // At v6 the `ref`/`readonly` StructModifier is absent, so `StructModifier*` matches zero and "struct"
    // is expected but the reserved `ref`/`readonly` is found -> the StructDeclaration fails.

    [TestMethod]
    public void RefStruct_RejectedAtV6()
        => AssertFailsV6("ref struct S { }");

    [TestMethod]
    public void ReadonlyStruct_RejectedAtV6()
        => AssertFailsV6("readonly struct S { }");

    // === NEGATIVE (version 7, malformed): missing name. ===
    // `ref struct { }` — the `ref` is a StructModifier, then `struct`, then the struct NAME (TypeName) is
    // expected but `{` is found (not an identifier) -> the StructDeclaration fails. (In real C# a struct
    // requires a name; this is a parse error.)

    [TestMethod]
    public void RefStruct_MissingName_Rejected()
        => AssertFailsV7("ref struct { }");

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
