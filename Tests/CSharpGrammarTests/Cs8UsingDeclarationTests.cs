using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.8.2 — C# 8.0 using declarations (Cs8.grammar). Positives use CreateParser(8) (Cs1+...+Cs8
// merged); version-purity negatives use CreateParser(7) (Cs1+...+Cs7, no CS8). See
// docs/CSharpParserPlan-progressT3.8.2.md for the hand-traces. The using declaration is a NEW
// STATEMENT form: `using Type Identifier = Expression ;` (or `using var Identifier = Expression ;`),
// re-declared on Statement (append, T0.3 merge). It is MUTUALLY EXCLUSIVE with the Cs1 UsingStatement
// ("using" "(" ResourceAcquisition ")" Statement) — after "using", a type/var (declaration) vs "("
// (statement). "using" is reserved (Cs1 ReservedKeyword:614), so no other Statement starts with it.
// The using STATEMENT (Cs1) is untouched (no regression at v1-v7).
[TestClass]
public class Cs8UsingDeclarationTests
{
    // === POSITIVE (version 8): basic using declaration. ===
    // `using FileStream f = new FileStream("a.txt");` — UsingDeclaration: "using", Type=FileStream
    // (a QualifiedName), f, =, new FileStream("a.txt") (a NewExpr Primary), ;. Roslyn
    // ParseLocalDeclarationStatement (LanguageParser.cs:10482).

    [TestMethod]
    public void UsingDeclaration_Basic_Succeeds()
        => AssertParsesV8("class C { void M() { using FileStream f = new FileStream(\"a.txt\"); } }");

    // === POSITIVE (version 8): using declaration with `var`. ===
    // `using var f = new FileStream("a.txt");` — UsingDeclarationType: "var" (Type FAILS — var is
    // reserved at v2+, Cs2:186-187, so it is not a TypeName). Roslyn IOperationTests_IUsingStatement.
    // cs:7967 (UsingDeclaration_SingleDeclaration, `using var c = new C();`).

    [TestMethod]
    public void UsingDeclaration_Var_Succeeds()
        => AssertParsesV8("class C { void M() { using var f = new FileStream(\"a.txt\"); } }");

    // === POSITIVE (version 8): using declaration with a simple (predefined) type. ===

    [TestMethod]
    public void UsingDeclaration_SimpleType_Succeeds()
        => AssertParsesV8("class C { void M() { using int x = 5; } }");

    // === POSITIVE (version 1 and 7, no-regression): the using STATEMENT (concrete type). ===
    // The using STATEMENT (Cs1 UsingStatement, "using" "(" ResourceAcquisition ")" Statement) is a
    // DIFFERENT rule from the using DECLARATION. It is untouched by Cs8, so it still parses at every
    // version. `using (int x = Foo()) { }` — the ResourceAcquisition is a ResourceDeclaration
    // (Type=int, VariableDeclarator=x = Foo()). Matches the pre-existing Cs1SwitchTryTests form.

    [TestMethod]
    public void UsingStatement_ConcreteType_ParsesAtV1()
        => AssertParsesV1("class C { void M() { using (int x = Foo()) { } } int Foo() { return 0; } }");

    [TestMethod]
    public void UsingStatement_ConcreteType_ParsesAtV7()
        => AssertParsesV7("class C { void M() { using (int x = Foo()) { } } int Foo() { return 0; } }");

    // === POSITIVE (version 1, no-regression): the using STATEMENT with `var`. ===
    // At v1 `var` is NOT reserved (it is reserved only at v2+, Cs2:186-187), so `var f = new X()` is
    // a ResourceDeclaration whose Type is `var` (a QualifiedName). This is the task's no-regression
    // form; it parses at v1. (At v2-v7 `var` is reserved and the ResourceAcquisition has no "var"
    // alternative, so the `var` form REJECTS there — a PRE-EXISTING limitation, not a regression from
    // T3.8.2; see the progress doc, Deviation D1. The concrete-type form above covers v1-v7.)

    [TestMethod]
    public void UsingStatement_Var_ParsesAtV1()
        => AssertParsesV1("class C { void M() { using (var f = new X()) { } } class X { } }");

    // === NEGATIVE (version 7, version-purity): the using declaration is not available at v7. ===
    // At v7 the UsingDeclaration is absent (Cs8-only). UsingStatement FAILS (no "(" after "using");
    // LocalVariableDeclaration/ExpressionStatement/LabeledStatement FAIL (the reserved "using" is not
    // a Type/expression-start/identifier); no other Statement starts with "using" -> REJECTS. At v8
    // it PARSES. Roslyn UsingDeclarationTests.cs:830 (UsingDeclarationsWithLangVer7_3, CS8652 at
    // C# 7.3).

    [TestMethod]
    public void UsingDeclaration_RejectedAtV7()
        => AssertFailsV7("class C { void M() { using FileStream f = new FileStream(\"a.txt\"); } }");

    // === NEGATIVE (version 8, malformed): missing initializer. ===
    // `using FileStream f;` — the UsingDeclaration REQUIRES "=" Expression after the identifier;
    // after `f` the next token is ";" (not "="), so the UsingDeclaration FAILS. UsingStatement FAILS
    // (no "(" after "using"). No other Statement starts with "using" -> REJECTS. (Roslyn reports a
    // missing-initializer error; this grammar rejects the form.)

    [TestMethod]
    public void UsingDeclaration_MissingInitializer_Rejected()
        => AssertFailsV8("class C { void M() { using FileStream f; } }");

    // === POSITIVE (version 8): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void UsingDeclaration_ExplicitType_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/IOperation/IOperation/IOperationTests_IUsingStatement.cs:8406
        // (UsingDeclaration_RegularAsync_Mix) — `using C c = new C();` (an explicit-type using
        // declaration). Adapted: the type is a user-defined name `C` (a QualifiedName), the
        // initializer is `new C()` (a NewExpr).
        AssertParsesV8("class C { void M() { using C c = new C(); } }");
    }

    [TestMethod]
    public void UsingDeclaration_NullInitializer_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Semantic/Semantics/UsingDeclarationTests.cs:838
        // (UsingDeclarationsWithLangVer7_3) — `using IDisposable x = null;` (a using declaration whose
        // initializer is the `null` literal, a Primary). Adapted: the type is a user-defined name `D`,
        // the initializer is `null`.
        AssertParsesV8("class C { void M() { using D x = null; } }");
    }

    [TestMethod]
    public void UsingDeclaration_CoexistsWithUsingStatement_Roslyn_Succeeds()
    {
        // Roslyn: IOperationTests_IUsingStatement.cs (the UsingDeclaration_Flow_* cases place using
        // declarations and using statements in the same block). Adapted: a block containing BOTH a
        // using declaration (`using A a = new A();`) and a using STATEMENT (`using (int b = 0) { }`)
        // — the two `using`-leading forms are mutually exclusive (type vs "(") and both parse.
        AssertParsesV8("class A { } class C { void M() { using A a = new A(); using (int b = 0) { } } }");
    }

    // === helpers ===

    private static void AssertParsesV8(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(8), input);

    private static void AssertFailsV8(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(8), input);

    private static void AssertParsesV7(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(7), input);

    private static void AssertFailsV7(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(7), input);

    private static void AssertParsesV1(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(1), input);

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
