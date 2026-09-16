using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.1.1.2 — C# 2.0 method type parameters + constraint clauses (Cs2.grammar). Positives use
// CreateParser(2) (Cs1+Cs2 merged); version-purity negatives use CreateParser(1) (Cs1 only) and
// must REJECT method type parameters and constraints. The re-declared Method / InterfaceMethod /
// type declarations REQUIRE ConstraintClause+ (see Cs2.grammar header and
// docs/CSharpParserPlan-progressT3.1.1.2.md for the mutual-exclusivity hand-traces).
[TestClass]
public class Cs2ConstraintTests
{
    // === POSITIVE (version 2): method constraints. ===

    [TestMethod]
    public void Method_StructConstraint_Succeeds()
        => AssertParsesV2("class C { void M<T>(T x) where T : struct { } }");

    [TestMethod]
    public void Method_InterfaceConstraint_Succeeds()
        => AssertParsesV2("class C { void M<T>(T x) where T : IBase { } }");

    [TestMethod]
    public void Method_NewConstraint_Succeeds()
        => AssertParsesV2("class C { void M<T>() where T : new() { } }");

    [TestMethod]
    public void Method_TwoConstraintClauses_Succeeds()
        => AssertParsesV2("class C { void M<T, U>() where T : U where U : struct { } }");

    [TestMethod]
    public void Method_CombinedConstraint_Succeeds()
        => AssertParsesV2("class C { void M<T>(T x) where T : IBase, new() { } }");

    [TestMethod]
    public void Method_ConstraintToEnclosingType_Succeeds()
        => AssertParsesV2("class C { void M<T>(T x) where T : C { } }");

    // === POSITIVE (version 2): type-level constraints. ===

    [TestMethod]
    public void Class_StructConstraint_Succeeds()
        => AssertParsesV2("class C<T> where T : struct { }");

    [TestMethod]
    public void Class_CombinedConstraint_Succeeds()
        => AssertParsesV2("class C<T> where T : IBase, new() { }");

    [TestMethod]
    public void Struct_StructConstraint_Succeeds()
        => AssertParsesV2("struct S<T> where T : struct { }");

    [TestMethod]
    public void Interface_StructConstraint_Succeeds()
        => AssertParsesV2("interface I<T> where T : struct { }");

    [TestMethod]
    public void Class_BaseListAndConstraint_Succeeds()
        => AssertParsesV2("class C<T> : Base<T> where T : struct { }");

    [TestMethod]
    public void Delegate_StructConstraint_Succeeds()
        => AssertParsesV2("delegate void D<T>(T x) where T : struct;");

    [TestMethod]
    public void InterfaceMethod_StructConstraint_Succeeds()
        => AssertParsesV2("interface I { void M<T>(T x) where T : struct; }");

    // === POSITIVE (version 2): Roslyn-derived, adapted. ===

    [TestMethod]
    public void Class_TypeConstraintBound_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs
        // TestClassWithTypeConstraintBound ("class a<b> where b : c { }").
        AssertParsesV2("class a<b> where b : c { }");
    }

    [TestMethod]
    public void Class_NewConstraintBound_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs
        // TestClassWithNewConstraintBound ("class a<b> where b : new() { }").
        AssertParsesV2("class a<b> where b : new() { }");
    }

    [TestMethod]
    public void Method_GenericTypeConstraintBound_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs
        // TestGenericClassMethodWithTypeConstraintBound ("class a { b X<c>() where b : d { } }").
        AssertParsesV2("class a { b X<c>() where b : d { } }");
    }

    [TestMethod]
    public void NonGenericClass_WithConstraint_Parses_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs
        // TestNonGenericClassWithTypeConstraintBound ("class a where b : c { }"). Roslyn parses it
        // at the syntax level and reports binder error CS0080 (constraints on non-generic decl);
        // the parser accepts it (binder concern), so this is a POSITIVE parse test.
        AssertParsesV2("class a where b : c { }");
    }

    [TestMethod]
    public void NonGenericMethod_WithConstraint_Parses_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs
        // TestNonGenericMethodWithTypeConstraintBound ("class a { void M() where b : c { } }").
        // Parses at the syntax level; binder error CS0080. Parser accepts (binder concern).
        AssertParsesV2("class a { void M() where b : c { } }");
    }

    // === NEGATIVE (version 1, version-purity): method generics / constraints must REJECT at v1. ===

    [TestMethod]
    public void Method_TypeParameter_RejectedAtV1()
        => AssertFailsV1("class C { void M<T>() { } }");

    [TestMethod]
    public void Method_WithConstraint_RejectedAtV1()
        => AssertFailsV1("class C { void M<T>(T x) where T : struct { } }");

    [TestMethod]
    public void Class_TypeConstraint_RejectedAtV1()
        => AssertFailsV1("class C<T> where T : struct { }");

    // === NEGATIVE (version 2, malformed): must REJECT at version 2. ===

    [TestMethod]
    public void Method_MissingColon_Rejected()
        => AssertFailsV2("class C { void M<T>() where T struct { } }");

    [TestMethod]
    public void Method_MissingName_Rejected()
        => AssertFailsV2("class C { void M<T>() where : struct { } }");

    [TestMethod]
    public void Method_MissingConstraint_Rejected()
        => AssertFailsV2("class C { void M<T>() where T : { } }");

    [TestMethod]
    public void Method_TrailingCommaConstraint_Rejected()
        => AssertFailsV2("class C { void M<T>() where T : struct, { } }");

    [TestMethod]
    public void Method_MissingParameterList_Rejected()
        => AssertFailsV2("class C { void M<T> where T : struct { } }");

    // === helpers ===

    private static void AssertParsesV2(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(2), input);

    private static void AssertFailsV2(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(2), input);

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
