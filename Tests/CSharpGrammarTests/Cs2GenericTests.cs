using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.1.1.1 — C# 2.0 type-level generics (Cs2.grammar). Positives use CreateParser(2) (Cs1+Cs2
// merged); version-purity negatives use CreateParser(1) (Cs1 only) and must REJECT generics.
// The re-declared rules follow the REQUIRED-new-construct principle (see Cs2.grammar header and
// docs/CSharpParserPlan-progressT3.1.1.1.md for the mutual-exclusivity hand-traces).
[TestClass]
public class Cs2GenericTests
{
    // === POSITIVE (version 2): generic type names, type-parameter lists, variance. ===

    [TestMethod]
    public void Class_SingleTypeParameter_Succeeds()
        => AssertParsesV2("class C<T> { }");

    [TestMethod]
    public void Class_MultipleTypeParameters_Succeeds()
        => AssertParsesV2("class C<T, U> { }");

    [TestMethod]
    public void Struct_TypeParameter_Succeeds()
        => AssertParsesV2("struct S<T> { }");

    [TestMethod]
    public void Interface_TypeParameter_Succeeds()
        => AssertParsesV2("interface I<T> { }");

    [TestMethod]
    public void Delegate_TypeParameter_Succeeds()
        => AssertParsesV2("delegate void D<T>(T x);");

    [TestMethod]
    public void Class_TypeParameter_FieldOfTypeParameter_Succeeds()
        => AssertParsesV2("class C<T> { T x; }");

    [TestMethod]
    public void Class_GenericField_Succeeds()
        => AssertParsesV2("class C<T> { C<T> y; }");

    [TestMethod]
    public void Class_GenericFieldWithConcreteArg_Succeeds()
        => AssertParsesV2("class C<T> { C<T, int> z; }");

    [TestMethod]
    public void Class_QualifiedGenericField_Succeeds()
        => AssertParsesV2("class C { System.Collections.Generic.List<int> x; }");

    [TestMethod]
    public void Interface_OutVariance_Succeeds()
        => AssertParsesV2("interface I<out T> { }");

    [TestMethod]
    public void Interface_InVariance_Succeeds()
        => AssertParsesV2("interface I<in T> { }");

    [TestMethod]
    public void Nested_GenericClass_Succeeds()
        => AssertParsesV2("class Outer { class Inner<T> { } }");

    // === POSITIVE (version 2): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void Interface_OutVariance_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs
        // TestGenericInterfaceWithAttributesAndVariance ("interface A<[B] out C> { }");
        // adapted: the attribute on the type parameter is dropped (not in T3.1.1.1 scope).
        AssertParsesV2("interface A<out C> { }");
    }

    [TestMethod]
    public void GenericNameWithTwoArguments_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/NameParsingTests.cs
        // TestGenericNameWithTwoArguments ("goo<bar,zed>"); adapted to a field.
        AssertParsesV2("class C { goo<bar, zed> x; }");
    }

    [TestMethod]
    public void NestedGenericName_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/NameParsingTests.cs
        // TestNestedGenericName_01 ("goo<bar<zed>>"); adapted to a field.
        AssertParsesV2("class C { goo<bar<zed>> x; }");
    }

    [TestMethod]
    public void LocalDeclarationWithGenericType_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/StatementParsingTests.cs
        // TestLocalDeclarationStatementWithGenericType ("T<a> b;"); adapted to a method body.
        AssertParsesV2("class C { void M() { T<a> b; } }");
    }

    // === NEGATIVE (version 1, version-purity): generics must REJECT at version 1. ===

    [TestMethod]
    public void Class_TypeParameter_RejectedAtV1()
        => AssertFailsV1("class C<T> { }");

    [TestMethod]
    public void Field_GenericType_RejectedAtV1()
        => AssertFailsV1("class D { C<T> x; }");

    [TestMethod]
    public void Interface_TypeParameter_RejectedAtV1()
        => AssertFailsV1("interface I<T> { }");

    // === NEGATIVE (version 2, malformed): must REJECT at version 2. ===

    [TestMethod]
    public void Class_EmptyTypeParameters_Rejected()
        => AssertFailsV2("class C<> { }");

    [TestMethod]
    public void Class_TrailingCommaTypeParameters_Rejected()
        => AssertFailsV2("class C<T,> { }");

    [TestMethod]
    public void Field_UnclosedTypeArgument_Rejected()
        => AssertFailsV2("class C { C< x; }");

    [TestMethod]
    public void Interface_VarianceWithoutName_Rejected()
        => AssertFailsV2("interface I<in> { }");

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
