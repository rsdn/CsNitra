using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.6.4 — C# 7.0 ref semantics (Cs7.grammar). Four CS7 features plus the `ref` EXPRESSION:
//   * `ref` return — `ref int M() { return _x; }` (a `ref` MethodModifier BEFORE the return type).
//   * `ref readonly` return — `ref readonly int M() { return _x; }` (the `ref readonly` MethodModifiers).
//   * `ref` local — `ref int x = ref _y;` (a `ref` modifier BEFORE the local variable's type).
//   * `out var` — `M(out var x)` (an `out` argument with an implicit type `var`).
//   * `ref` expression — `ref _y` (a `ref` prefix operator, like the Cs5 `await`).
// Positives use CreateParser(7) (Cs1+...+Cs7 merged); version-purity negatives use CreateParser(6)
// (Cs1+...+Cs6, no CS7). See docs/CSharpParserPlan-progressT3.6.4.md for the hand-traces.
[TestClass]
public class Cs7RefTests
{
    // === POSITIVE (version 7): ref return. ===
    // The `ref` is a MethodModifier (Cs7 re-declaration) BEFORE the return type. Roslyn
    // ParseReturnType (LanguageParser.cs:3314) + RefReadonlyTests.cs:28.

    [TestMethod]
    public void RefReturn_Succeeds()
        => AssertParsesV7("class C { ref int M() { return _x; } int _x; }");

    // === POSITIVE (version 7): ref readonly return. ===
    // The `ref readonly` are two MethodModifiers BEFORE the return type. NOTE (Deviation D1): `ref
    // readonly` is a C# 7.2 feature in Roslyn (RefReadonlyTests.cs:48, ERR_FeatureNotAvailable-
    // InVersion7_1 "readonly references ... 7.2"), but the task requires it in CS7 (the version gate
    // is a binder concern; the PARSER accepts the form).

    [TestMethod]
    public void RefReadonlyReturn_Succeeds()
        => AssertParsesV7("class C { ref readonly int M() { return _x; } int _x; }");

    // === POSITIVE (version 7): ref local. ===
    // The `ref` is a modifier BEFORE the local variable's type (a new LocalVariableDeclaration
    // alternative). The initializer `ref _y` is a RefExpression. Roslyn StatementParsingTests.cs:793
    // (TestRefLocalDeclarationStatementWithInitializer, `ref T a = ref b;`).

    [TestMethod]
    public void RefLocal_Succeeds()
        => AssertParsesV7("class C { void M() { ref int x = ref _y; } int _y; }");

    // === POSITIVE (version 7): out var. ===
    // The argument is `out` + `var` (implicit type) + `x` (name) — a new Argument alternative. The
    // `out` in the DECLARATION (`void N(out int x)`) is a ParameterModifier (C# 1.0, already handled).
    // Roslyn DeclarationParsingTests.cs:6580 (ParseOutVar, `M(out var x);`).

    [TestMethod]
    public void OutVar_Succeeds()
        => AssertParsesV7("class C { void M() { N(out var x); } void N(out int x) { } }");

    // === POSITIVE (version 7): ref expression in a return. ===
    // `return ref _x;` — the `ref _x` is a RefExpr (a `ref` prefix operator) in the ReturnStatement's
    // Expression. Roslyn ParsePrimaryExpressionWithoutPostfix (LanguageParser.cs:12076): a `ref` token
    // starts a RefExpression(refKeyword, ParseExpressionCore()).

    [TestMethod]
    public void RefExpressionInReturn_Succeeds()
        => AssertParsesV7("class C { ref int M() { return ref _x; } int _x; }");

    // === POSITIVE (version 7): ref local without an initializer. ===
    // Roslyn StatementParsingTests.cs:767 (TestRefLocalDeclarationStatement, `ref T a;`).

    [TestMethod]
    public void RefLocal_NoInitializer_Succeeds()
        => AssertParsesV7("class C { void M() { ref int x; } }");

    // === POSITIVE (version 7): a `ref` argument (via the RefExpr). ===
    // `N(ref _x)` — the argument `ref _x` is a RefExpr, so it is a valid PositionalArgument (Cs4
    // Argument). This documents that a `ref` argument parses at v7 (a C# 1.0 feature; at v6 it rejects
    // because the RefExpr is absent — see the version-purity note in the progress doc).

    [TestMethod]
    public void RefArgument_Succeeds()
        => AssertParsesV7("class C { void M() { N(ref _x); } void N(ref int x) { } }");

    // === POSITIVE (version 7): the task's "missing type" malformed case PARSES as an assignment. ===
    // NOTE (Deviation D3): `ref x = ref _y;` is NOT a ref local (the `RefLocalDeclaration` alternative
    // fails — `x` is parsed as a user-defined Type, then `=` is not a VariableDeclarator start). Instead
    // it is an ExpressionStatement: an assignment `(ref x) = (ref _y)` (each side a RefExpr). In real
    // C# it is a binder error (you cannot assign to a `ref` expression), but a valid PARSE. So it is a
    // POSITIVE test (it parses), not the NEGATIVE the task expected.

    [TestMethod]
    public void RefExpressionAssignment_Succeeds()
        => AssertParsesV7("class C { void M() { ref x = ref _y; } }");

    // === POSITIVE (version 6): no-regression — existing argument forms still parse at v6. ===
    // The task's "POSITIVE (version 6) `N(out x)`" is based on a FALSE PREMISE (Deviation D2): the
    // plain `out x` / `ref x` argument forms are a PRE-EXISTING GAP (they do not parse at ANY version
    // currently — the Cs4 Argument has only NamedArgument/PositionalArgument). This test instead
    // verifies the EXISTING argument forms (positional + named) still parse at v6 (no regression from
    // the new `out var` Argument alternative).

    [TestMethod]
    public void ArgumentForms_ParsesAtV6()
        => AssertParsesV6("class C { void M() { N(x); N(y: 5); } }");

    // === POSITIVE (version 6): no-regression — a `readonly` field still parses at v6. ===
    // Adding `readonly` to the Cs7 MethodModifier must not break `readonly` fields (a FieldModifier,
    // Cs1.grammar:162 — a DIFFERENT rule). At v6 `readonly` is only a FieldModifier; at v7 it is also a
    // MethodModifier, but the field (a `;` after the name) is disambiguated from a method (a `(` after
    // the name). Verified: parses at both v6 and v7.

    [TestMethod]
    public void ReadonlyField_ParsesAtV6()
        => AssertParsesV6("class C { readonly int F; }");

    // === POSITIVE (version 7): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void RefLocal_NoInit_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/StatementParsingTests.cs:767
        // (TestRefLocalDeclarationStatement) — `ref T a;` (a ref local with no initializer). Adapted to
        // a full method body.
        AssertParsesV7("class C { void M() { ref T a; } }");
    }

    [TestMethod]
    public void RefLocal_Initializer_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/StatementParsingTests.cs:793
        // (TestRefLocalDeclarationStatementWithInitializer) — `ref T a = ref b;` (the initializer is a
        // RefExpression `ref b`, asserted at :816-817). Adapted to a full method body.
        AssertParsesV7("class C { void M() { ref T a = ref b; } T b; }");
    }

    [TestMethod]
    public void OutVar_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs:6580 (ParseOutVar)
        // — `M(out var x);`. Adapted to a full method body + the `out` declaration.
        AssertParsesV7("class C { void Goo() { M(out var x); } void M(out int x) { } }");
    }

    [TestMethod]
    public void RefReadonlyReturn_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/RefReadonlyTests.cs:28
        // (RefReadonlyReturn_CSharp7) — `static ref readonly T M<T>() { return ref ...; }`. Adapted: a
        // `static ref readonly` generic method with a block body (the `static`/`ref`/`readonly` are all
        // MethodModifiers; `M<T>` is the greedy TypeName; `return _x` is a plain ReturnStatement).
        AssertParsesV7("class C { static ref readonly T M<T>() { return _x; } T _x; }");
    }

    // === NEGATIVE (version 6, version-purity): the CS7 ref forms are not available at v6. ===
    // At v6 `ref`/`readonly` are not MethodModifiers, "ref" is not a local-declaration modifier, the
    // "out var" Argument alternative is absent, and the RefExpr is absent — so every form REJECTS.

    [TestMethod]
    public void RefReturn_RejectedAtV6()
        => AssertFailsV6("class C { ref int M() { } }");

    [TestMethod]
    public void RefLocal_RejectedAtV6()
        => AssertFailsV6("class C { void M() { ref int x = ref _y; } }");

    [TestMethod]
    public void OutVar_RejectedAtV6()
        => AssertFailsV6("class C { void M() { N(out var x); } }");

    [TestMethod]
    public void RefReadonly_RejectedAtV6()
        => AssertFailsV6("class C { ref readonly int M() { } }");

    // === NEGATIVE (version 7, malformed). ===

    // Missing return type: `ref M() { }` — `ref` is a MethodModifier, then `M` is parsed as the return
    // Type (a user-defined type), and `(` is found where the method NAME (TypeName) is expected -> the
    // Method alternative fails; no other member alternative matches -> REJECTS.
    [TestMethod]
    public void RefReturn_MissingType_Rejected()
        => AssertFailsV7("class C { ref M() { } }");

    // Missing initializer expression: `ref int x = ;` — the `VariableDeclarator`'s `("= Expression)?`
    // consumes the `=` but the Expression fails on `;` (backtracks to no initializer), then `=` is found
    // where `;`/`,` is expected -> the RefLocalDeclaration fails; ExpressionStatement fails (`ref int`
    // is not an Expression) -> REJECTS.
    [TestMethod]
    public void RefLocal_MissingInitializer_Rejected()
        => AssertFailsV7("class C { void M() { ref int x = ; } }");

    // === helpers ===

    private static void AssertParsesV7(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(7), input);

    private static void AssertFailsV7(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(7), input);

    private static void AssertParsesV6(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(6), input);

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
