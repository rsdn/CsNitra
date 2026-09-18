using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.12.1 — C# 12.0 primary constructors for classes (Cs12.grammar). Positives use CreateParser(12)
// (Cs1+...+Cs12 merged); version-purity negatives use CreateParser(11) (Cs1+...+Cs11, no CS12). See
// docs/CSharpParserPlan-progressT3.12.1.md. A class primary constructor is a class declaration with a
// parameter list (like a record's positional parameter list): `class` + modifiers + name + REQUIRED
// param list + optional base list + body. The REQUIRED param list makes it mutually exclusive with the
// Cs1 (no param list) and Cs2 (REQUIRED TypeParameterList) ClassDeclaration alternatives, so it is
// reachable only at CS12.
[TestClass]
public class Cs12PrimaryConstructorTests
{
    // === POSITIVE (version 12): simple class with a primary constructor and a body. ===
    // Roslyn paramList (LanguageParser.cs:1827-1828) + ClassKeyword case (1974-1988).

    [TestMethod]
    public void ClassPrimaryConstructor_Simple_Succeeds()
        => AssertParsesV12("class MyClass(int x, string y) { }");

    // === POSITIVE (version 12): class with a primary constructor and a base list. ===
    // Roslyn baseList (LanguageParser.cs:1830).

    [TestMethod]
    public void ClassPrimaryConstructor_WithBase_Succeeds()
        => AssertParsesV12("class MyClass(int x) : Base { }");

    // === POSITIVE (version 12): class with a primary constructor and a body member. ===

    [TestMethod]
    public void ClassPrimaryConstructor_WithBodyMember_Succeeds()
        => AssertParsesV12("class MyClass(int x) { int Z; }");

    // === POSITIVE (version 12): empty primary constructor (requireOneElement:false, 4750). ===

    [TestMethod]
    public void ClassPrimaryConstructor_Empty_Succeeds()
        => AssertParsesV12("class MyClass() { }");

    // === POSITIVE (version 12): access modifier before the class. ===

    [TestMethod]
    public void ClassPrimaryConstructor_Public_Succeeds()
        => AssertParsesV12("public class MyClass(int x) { }");

    // === POSITIVE (version 12): multiple base types. ===

    [TestMethod]
    public void ClassPrimaryConstructor_MultipleBases_Succeeds()
        => AssertParsesV12("class MyClass(int x) : Base1, Base2 { }");

    // === POSITIVE (version 12): generic class with a primary constructor (greedy TypeName, Cs2 D1). ===

    [TestMethod]
    public void ClassPrimaryConstructor_Generic_Succeeds()
        => AssertParsesV12("class MyClass<T>(int x) { }");

    // === POSITIVE (version 12): nested class with a primary constructor (ClassMember -> TypeDeclaration). ===

    [TestMethod]
    public void ClassPrimaryConstructor_Nested_Succeeds()
        => AssertParsesV12("class Outer { class Inner(int x) { } }");

    // === POSITIVE (version 12): class with a primary constructor inside a namespace. ===

    [TestMethod]
    public void ClassPrimaryConstructor_InNamespace_Succeeds()
        => AssertParsesV12("namespace N { class C(int x) { } }");

    // === POSITIVE (version 12): a REGULAR class (no primary constructor) still parses at v12. ===
    // Regression: the Cs1 ClassDeclaration alternative (no param list) must remain reachable.

    [TestMethod]
    public void RegularClass_StillSucceedsAtV12()
        => AssertParsesV12("class MyClass { }");

    // === POSITIVE (version 12): a REGULAR generic class (no primary constructor) still parses at v12. ===

    [TestMethod]
    public void RegularGenericClass_StillSucceedsAtV12()
        => AssertParsesV12("class MyClass<T> { }");

    // === NEGATIVE (version 11, version-purity): primary constructors are not available at v11. ===
    // At v11 the Cs12 ClassDeclaration alternative is ABSENT (Cs12-only); the Cs1/Cs2 alternatives
    // both fail on the "(" after the name -> the declaration is unconsumed -> REJECTS. At v12 it PARSES.

    [TestMethod]
    public void ClassPrimaryConstructor_RejectedAtV11()
        => AssertFailsV11("class MyClass(int x, string y) { }");

    // === NEGATIVE (version 11, version-purity): with a base list at v11. ===

    [TestMethod]
    public void ClassPrimaryConstructor_WithBase_RejectedAtV11()
        => AssertFailsV11("class MyClass(int x) : Base { }");

    // === NEGATIVE (version 11, version-purity): empty primary constructor at v11. ===

    [TestMethod]
    public void ClassPrimaryConstructor_Empty_RejectedAtV11()
        => AssertFailsV11("class MyClass() { }");

    // === NEGATIVE (version 12, malformed): missing close paren in the parameter list. ===

    [TestMethod]
    public void ClassPrimaryConstructor_MissingCloseParen_Rejected()
        => AssertFailsV12("class MyClass(int x;");

    // === helpers ===

    private static void AssertParsesV12(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(12), input);

    private static void AssertFailsV12(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(12), input);

    private static void AssertFailsV11(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(11), input);

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
