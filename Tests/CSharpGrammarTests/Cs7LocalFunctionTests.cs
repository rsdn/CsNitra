using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.6.3 — C# 7.0 local functions (Cs7.grammar). A local function is a method-like declaration
// INSIDE a block (a statement, not a member): LocalFunctionModifier* Type TypeName "("
// ParameterList? ")" ConstraintClause* MethodBody. Positives use CreateParser(7) (Cs1+...+Cs7
// merged); version-purity negatives use CreateParser(6) (Cs1+...+Cs6, no CS7). See
// docs/CSharpParserPlan-progressT3.6.3.md for the hand-traces. The REQUIRED-new-construct is the
// "(" ParameterList? ")" after the name, which no other Cs1 Statement form has at that position, so
// the local function is mutually exclusive with LocalVariableDeclaration / ExpressionStatement /
// LabeledStatement for the task's forms.
[TestClass]
public class Cs7LocalFunctionTests
{
    // === POSITIVE (version 7): basic local function. ===

    [TestMethod]
    public void LocalFunction_Basic_Succeeds()
        => AssertParsesV7("class C { void M() { void N() { } N(); } }");

    // === POSITIVE (version 7): local function with a return type. ===

    [TestMethod]
    public void LocalFunction_ReturnType_Succeeds()
        => AssertParsesV7("class C { void M() { int N() { return 5; } } }");

    // === POSITIVE (version 7): local function with modifiers (static / async / unsafe). ===
    // static local functions are a C# 8.0 feature in Roslyn (IDS_FeatureStaticLocalFunctions), but
    // the task requires them in CS7 (progress doc, Deviation D2 — the version gate is a binder
    // concern, not a parse concern).

    [TestMethod]
    public void LocalFunction_StaticModifier_Succeeds()
        => AssertParsesV7("class C { void M() { static int N() { return 5; } } }");

    [TestMethod]
    public void LocalFunction_AsyncModifier_Succeeds()
        => AssertParsesV7("class C { void M() { async void N() { } } }");

    [TestMethod]
    public void LocalFunction_UnsafeModifier_Succeeds()
        => AssertParsesV7("class C { void M() { unsafe void N() { } } }");

    // === POSITIVE (version 7): local function with type parameters (greedy TypeName, T3.1.1.1 D1). ===
    // NOTE (Deviation D4): the task's form is `void N<T>() { } N<int>();`, but the generic INVOCATION
    // `N<int>()` is a pre-existing grammar gap (TypeArgumentList only appears in type positions via the
    // greedy TypeName, not in Primary/Expression), so it does not parse at ANY version (verified: fails
    // at v6). The local function DECLARATION `void N<T>() { }` is the CS7 feature tested here; the
    // invocation is a separate CS2 concern (out of scope for T3.6.3).

    [TestMethod]
    public void LocalFunction_Generics_Succeeds()
        => AssertParsesV7("class C { void M() { void N<T>() { } } }");

    // === POSITIVE (version 7): local function with an expression body (T3.5.3 MethodBody "=>"). ===

    [TestMethod]
    public void LocalFunction_ExpressionBody_Succeeds()
        => AssertParsesV7("class C { void M() { int N() => 5; } }");

    // === POSITIVE (version 7): local function with a constraint clause (T3.1.1.2 ConstraintClause). ===

    [TestMethod]
    public void LocalFunction_Constraints_Succeeds()
        => AssertParsesV7("class C { void M() { void N<T>() where T : struct { } } }");

    // === POSITIVE (version 7): nested local functions. ===

    [TestMethod]
    public void LocalFunction_Nested_Succeeds()
        => AssertParsesV7("class C { void M() { void N() { void O() { } O(); } N(); } }");

    // === POSITIVE (version 7): local function with a parameter list. ===

    [TestMethod]
    public void LocalFunction_ParameterList_Succeeds()
        => AssertParsesV7("class C { void M() { int N(int x) { return x; } } }");

    // === POSITIVE (version 7): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void LocalFunction_StaticAsyncModifierOrder_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/LocalFunctionParsingTests.cs:1794
        // (AsyncStaticFunctions) — `static async void F1() { }` (modifier order static-then-async).
        // Ordering is NOT enforced (a greedy any-order loop). Adapted to a full method body.
        AssertParsesV7("class C { void M() { static async void F1() { } } }");
    }

    [TestMethod]
    public void LocalFunction_AsyncStaticModifierOrder_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/LocalFunctionParsingTests.cs:1794
        // (AsyncStaticFunctions) — `async static void F2() { }` (modifier order async-then-static).
        // Ordering is NOT enforced. Adapted to a full method body.
        AssertParsesV7("class C { void M() { async static void F2() { } } }");
    }

    [TestMethod]
    public void LocalFunction_GenericConstraint_ExpressionBody_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/LocalFunctionParsingTests.cs:1155
        // (LocalFuncWithWhitespace) — `int goo<T>() where T : IFace => 5;` (type parameters +
        // constraint + expression body). Adapted to a full method body.
        AssertParsesV7("class C { void M() { int goo<T>() where T : IFace => 5; } }");
    }

    [TestMethod]
    public void LocalFunction_GenericConstraint_BlockBody_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/LocalFunctionParsingTests.cs:1155
        // (LocalFuncWithWhitespace) — `int goo<T>() where T : IFace { return 5; }` (type parameters
        // + constraint + block body). Adapted to a full method body.
        AssertParsesV7("class C { void M() { int goo<T>() where T : IFace { return 5; } } }");
    }

    // === POSITIVE (version 6): a regular local variable declaration must stay green (no regression
    // from the new local-function Statement alternative). ===

    [TestMethod]
    public void LocalVariableDeclaration_ParsesAtV6()
        => AssertParsesV6("class C { void M() { int x = 5; } }");

    // === NEGATIVE (version 6, version-purity): a local function is not available at v6. ===
    // At v6 `void N() { }` is neither a LocalVariableDeclaration (needs ";" or "=" after the name)
    // nor an ExpressionStatement (a bare type is not an expression start) -> the block fails.

    [TestMethod]
    public void LocalFunction_RejectedAtV6()
        => AssertFailsV6("class C { void M() { void N() { } } }");

    // === NEGATIVE (version 7, malformed). ===

    // Missing body: `void N()` is followed by `}` (the block end), which is not a MethodBody
    // ({ / ; / =>). No Statement alternative matches `void N()` -> the block fails.
    [TestMethod]
    public void LocalFunction_MissingBody_Rejected()
        => AssertFailsV7("class C { void M() { void N() } }");

    // Missing parameter list: `void N` is followed by `{`, which is not "(" (LocalFunctionStatement)
    // nor ";" / "=" (LocalVariableDeclaration) -> no Statement alternative matches -> the block fails.
    [TestMethod]
    public void LocalFunction_MissingParameterList_Rejected()
        => AssertFailsV7("class C { void M() { void N { } } }");

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
