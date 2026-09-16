using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.3.1 — C# 4.0 `dynamic` (predefined type) (Cs4.grammar). Positives use CreateParser(4)
// (Cs1+Cs2+Cs3+Cs4 merged); version-purity uses CreateParser(3) (Cs1+Cs2+Cs3, no CS4).
// `dynamic` is a CONTEXTUAL keyword: a plain identifier (a valid type name) in C# 1.0–3.0, a keyword
// in C# 4.0+. At v4 it is a PredefinedType (and reserved, so the TypeName route of `Type` fails — no
// equal-length tie); at v3 it is a TypeName (parses as a type-name declaration, like `var` at v1).
// See docs/CSharpParserPlan-progressT3.3.1.md for the hand-traces.
[TestClass]
public class Cs4DynamicTests
{
    // === POSITIVE (version 4): `dynamic` as a type in every type position. ===

    [TestMethod]
    public void Dynamic_Field_Succeeds()
        => AssertParsesV4("class C { dynamic x = 5; }");

    [TestMethod]
    public void Dynamic_MethodParam_Succeeds()
        => AssertParsesV4("class C { void M(dynamic x) { } }");

    [TestMethod]
    public void Dynamic_ReturnType_Succeeds()
        => AssertParsesV4("class C { dynamic M() { return 5; } }");

    [TestMethod]
    public void Dynamic_Local_Succeeds()
        => AssertParsesV4("class C { void M() { dynamic y = x; } }");

    [TestMethod]
    public void Dynamic_AutoProperty_Succeeds()
        => AssertParsesV4("class C { dynamic P { get; set; } }");

    // === POSITIVE (version 4): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void Dynamic_Local_NoInitializer_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/StatementParsingTests.cs:313
        // TestLocalDeclarationStatementWithDynamic — "dynamic a;" (no initializer; the special dynamic
        // handling is a binder concern, so it parses at the syntax level).
        AssertParsesV4("class C { void M() { dynamic a; } }");
    }

    [TestMethod]
    public void Dynamic_ParamsArray_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/RoundTrippingTests.cs:1529
        // "public delegate Y @dynamic<X, Y>(X u, params dynamic[] ary);" — params dynamic[] (an array
        // of dynamic as a parameter type). Adapted to a method (no verbatim @dynamic, no generics).
        AssertParsesV4("class C { void M(params dynamic[] args) { } }");
    }

    [TestMethod]
    public void Dynamic_DelegateParam_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/RoundTrippingTests.cs:1528
        // "public delegate void TypeName<T>(ref T t, dynamic d);" — a `dynamic d` parameter.
        AssertParsesV4("class C { delegate void D(dynamic d); }");
    }

    // === VERSION-PURITY: `dynamic` is a valid type name at v3, reserved (a predefined type) at v4. ===

    // A `dynamic`-typed FIELD parses at v3 (dynamic is a valid TypeName) as a type-NAME declaration
    // (like `var x = 5;` at v1); the same input parses at v4 as a predefined-type field. Both parse —
    // the interpretation differs (documented, not a reject).
    [TestMethod]
    public void Dynamic_AsTypeName_Field_ParsesAtV3()
        => AssertParsesV3("class C { dynamic x = 5; }");

    // A field NAMED `dynamic` parses at v3 (dynamic is a plain identifier, not reserved) but REJECTS
    // at v4 (dynamic is reserved -> not a valid name). This is the clean v3-vs-v4 discriminator
    // (analogous to the `var`-typed field in T3.1.2).
    [TestMethod]
    public void Dynamic_AsFieldName_ParsesAtV3()
        => AssertParsesV3("class C { int dynamic; }");

    [TestMethod]
    public void Dynamic_AsFieldName_RejectedAtV4()
        => AssertFailsV4("class C { int dynamic; }");

    // === NEGATIVE (version 4, malformed): must REJECT at v4. ===

    [TestMethod]
    public void Dynamic_MissingIdentifier_Rejected()
        => AssertFailsV4("class C { void M() { dynamic = 5; } }");

    [TestMethod]
    public void Dynamic_WithoutName_Rejected()
        => AssertFailsV4("class C { dynamic; }");

    // === helpers ===

    private static void AssertParsesV4(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(4), input);

    private static void AssertFailsV4(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(4), input);

    private static void AssertParsesV3(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(3), input);

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
