using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.1.2 — C# 2.0 `var` (implicit typing) + anonymous methods (Cs2.grammar). Positives use
// CreateParser(2) (Cs1+Cs2 merged); version-purity negatives use CreateParser(1) (Cs1 only).
// `var` is a plain identifier in C# 1.0 (a valid type name) but a reserved keyword in C# 2.0, so
// the version-purity discriminator is a `var`-typed FIELD (parses at v1, rejects at v2); a `var`
// LOCAL parses at both (typed declaration at v1, implicit typing at v2). Anonymous methods are
// v2-only expressions. See docs/CSharpParserPlan-progressT3.1.2.md for the hand-traces.
[TestClass]
public class Cs2VarAnonymousMethodTests
{
    // === POSITIVE (version 2): `var` implicit local variable typing. ===

    [TestMethod]
    public void Var_SingleLocal_Succeeds()
        => AssertParsesV2("class C { void M() { var x = 5; } }");

    [TestMethod]
    public void Var_MultipleLocals_Succeeds()
        => AssertParsesV2("class C { void M() { var a = 1, b = 2; } }");

    // `var x;` (no initializer) parses at the syntax level; a binder error CS0815 in real C#
    // (parser-vs-binder split).
    [TestMethod]
    public void Var_NoInitializer_Succeeds()
        => AssertParsesV2("class C { void M() { var x; } }");

    [TestMethod]
    public void Var_InLoopBody_Succeeds()
        => AssertParsesV2("class C { void M() { for (int i = 0; i < 10; i++) { var x = i; } } }");

    [TestMethod]
    public void Var_InIfBody_Succeeds()
        => AssertParsesV2("class C { void M() { if (true) { var x = 5; } } }");

    // === POSITIVE (version 2): anonymous methods (expression). ===

    [TestMethod]
    public void AnonymousMethod_NoParams_Succeeds()
        => AssertParsesV2("class C { void M() { Run(delegate { }); } }");

    [TestMethod]
    public void AnonymousMethod_EmptyParamList_Succeeds()
        => AssertParsesV2("class C { void M() { Run(delegate () { }); } }");

    [TestMethod]
    public void AnonymousMethod_TypedParam_Succeeds()
        => AssertParsesV2("class C { void M() { Run(delegate (int x) { }); } }");

    // Untyped parameter: parse-level form (implicit-typed anonymous-method parameters are a C# 4.0
    // binder/version concern; the parser accepts the syntactic form — parser-vs-binder split).
    [TestMethod]
    public void AnonymousMethod_UntypedParam_Succeeds()
        => AssertParsesV2("class C { void M() { Run(delegate (x) { }); } }");

    [TestMethod]
    public void AnonymousMethod_MultipleTypedParams_Succeeds()
        => AssertParsesV2("class C { void M() { Run(delegate (int x, int y) { }); } }");

    [TestMethod]
    public void AnonymousMethod_RefParam_Succeeds()
        => AssertParsesV2("class C { void M() { Run(delegate (ref int x) { }); } }");

    [TestMethod]
    public void AnonymousMethod_WithBodyStatement_Succeeds()
        => AssertParsesV2("class C { void M() { Run(delegate { x = 5; }); } }");

    // === POSITIVE (version 2): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void Var_AnonymousMethodCombined_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs:12669
        // "var f1 = delegate { return 42; };" — var + anonymous method in one local declaration.
        AssertParsesV2("class C { void M() { var f = delegate { return 42; }; } }");
    }

    [TestMethod]
    public void Var_AnonymousMethodTypedParam_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs:12670
        // "var f2 = delegate (int x) { return x * 2; };".
        AssertParsesV2("class C { void M() { var f = delegate (int x) { return x * 2; }; } }");
    }

    [TestMethod]
    public void AnonymousMethod_RefParam_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs:12787
        // "var f = delegate (ref int i) { i = 42; };".
        AssertParsesV2("class C { void M() { var f = delegate (ref int i) { i = 42; }; } }");
    }

    // === VERSION-PURITY: `var` is a valid type name at v1, reserved (not a type) at v2. ===

    // A `var`-typed FIELD parses at v1 (var is a valid TypeName) and rejects at v2 (var is reserved,
    // not a Type; there is no var-field rule). This is the clean v1-vs-v2 discriminator.
    [TestMethod]
    public void Var_AsTypeName_Field_ParsesAtV1()
        => AssertParsesV1("class C { var X; }");

    [TestMethod]
    public void Var_AsTypeName_Field_RejectedAtV2()
        => AssertFailsV2("class C { var X; }");

    // A `var` LOCAL parses at v1 as a *typed* declaration whose type is `var` (var is a valid
    // TypeName there); the same input parses at v2 as implicit typing. Both parse — the
    // interpretation differs (documented, not a reject).
    [TestMethod]
    public void Var_Local_ParsesAtV1_AsTypedDeclaration()
        => AssertParsesV1("class C { void M() { var x = 5; } }");

    // === NEGATIVE (version 1, version-purity): anonymous methods must REJECT at v1. ===

    [TestMethod]
    public void AnonymousMethod_RejectedAtV1()
        => AssertFailsV1("class C { void M() { Run(delegate { }); } }");

    // === NEGATIVE (version 2, malformed): must REJECT at v2. ===

    [TestMethod]
    public void Var_MissingIdentifier_Rejected()
        => AssertFailsV2("class C { void M() { var = 5; } }");

    [TestMethod]
    public void Var_MissingInitializerExpression_Rejected()
        => AssertFailsV2("class C { void M() { var x = ; } }");

    [TestMethod]
    public void AnonymousMethod_UnclosedBlock_Rejected()
        => AssertFailsV2("class C { void M() { Run(delegate { ); } }");

    [TestMethod]
    public void AnonymousMethod_UnclosedParamList_Rejected()
        => AssertFailsV2("class C { void M() { Run(delegate (int x { ); } }");

    // === helpers ===

    private static void AssertParsesV2(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(2), input);

    private static void AssertFailsV2(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(2), input);

    private static void AssertParsesV1(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(1), input);

    private static void AssertFailsV1(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(1), input);

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
