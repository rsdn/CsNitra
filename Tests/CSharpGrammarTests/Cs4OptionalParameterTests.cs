using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.3.3 — C# 4.0 optional parameters (Cs4.grammar). Positives use CreateParser(4) (Cs1+Cs2+Cs3+Cs4
// merged); version-purity negatives use CreateParser(3) (Cs1+Cs2+Cs3, no CS4).
//
// An optional parameter is `Type Identifier = Expression`. The Cs1 `Parameter` (Cs1.grammar:448) is
// `Attributes? ParameterModifier* Type TypeName` (no default value); Cs4 re-declares `Parameter` to
// append `Attributes? ParameterModifier* Type TypeName "=" Expression : Comma`. The re-declared
// alternative REQUIRES the `= Expression : Comma` (the REQUIRED-new-construct), so it is mutually
// exclusive with the Cs1 one: `int x` -> Cs1 (no `=`); `int x = 5` -> Cs4 (longest-match). The default
// value is `Expression : Comma` (minPrecedence 1) so it stops at the `ParameterList` `,` separator
// (the TDOPP Comma fix, T3.3.2). The "optional after required" rule (CS1737) is a BINDER concern — the
// parser accepts any order. See docs/CSharpParserPlan-progressT3.3.3.md for the hand-traces.
[TestClass]
public class Cs4OptionalParameterTests
{
    // === POSITIVE (version 4): a single optional parameter. ===

    [TestMethod]
    public void OptionalParameter_Single_Succeeds()
        => AssertParsesV4("class C { void M(int x = 5) { } }");

    // === POSITIVE (version 4): multiple optional parameters. ===

    [TestMethod]
    public void OptionalParameter_Multiple_Succeeds()
        => AssertParsesV4("class C { void M(int x = 5, int y = 6) { } }");

    // === POSITIVE (version 4): a required parameter before an optional one. ===

    [TestMethod]
    public void OptionalParameter_RequiredBeforeOptional_Succeeds()
        => AssertParsesV4("class C { void M(int x, int y = 6) { } }");

    // === POSITIVE (version 4): the default value is `null`. ===

    [TestMethod]
    public void OptionalParameter_NullDefault_Succeeds()
        => AssertParsesV4("class C { void M(string s = null) { } }");

    // === POSITIVE (version 4): the default value is a complex expression (a method call). ===

    [TestMethod]
    public void OptionalParameter_ComplexDefault_Succeeds()
        => AssertParsesV4("class C { void M(int x = Compute()) { } }");

    // === POSITIVE (version 4): the optional parameter is used in the method body. ===

    [TestMethod]
    public void OptionalParameter_UseTheParameter_Succeeds()
        => AssertParsesV4("class C { void M(int x = 5) { N(x); } }");

    // === POSITIVE (version 4): the `Expression : Comma` fix — a qualified type name as the second
    // parameter's type. A plain `Expression` default value would absorb the `,` and parse
    // `System.String` as a comma-expression RHS (member access), leaving `s` dangling -> failure.
    // `Expression : Comma` stops at the `,`, so the second parameter parses. ===

    [TestMethod]
    public void OptionalParameter_QualifiedTypeSecondParam_Succeeds()
        => AssertParsesV4("class C { void M(int x = 5, System.String s) { } }");

    // === POSITIVE (version 4): the parser accepts ANY order (optional before required). The
    // "optional after required" rule (CS1737) is a BINDER concern (Roslyn ParameterHelpers.cs:888),
    // not a parse error — so this parses at v4. ===

    [TestMethod]
    public void OptionalParameter_AnyOrder_Succeeds()
        => AssertParsesV4("class C { void M(int y = 6, int x) { } }");

    // === POSITIVE (version 4): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void OptionalParameter_Single_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParserErrorMessageTests.cs:6232
        // OptionalParameterBeforeCSharp4 — "void M(int x = 1) { }" (the exact input; a single optional
        // parameter with the integer default `1`).
        AssertParsesV4("class C { void M(int x = 1) { } }");
    }

    [TestMethod]
    public void OptionalParameter_Multiple_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/RoundTrippingTests.cs:1497
        // RegressError4AttributeWithNamedParam — "public TestAttribute(int i = 0, int j = 1) { }" (a
        // constructor with two optional parameters). Adapted to a method.
        AssertParsesV4("class C { void TestAttribute(int i = 0, int j = 1) { } }");
    }

    [TestMethod]
    public void OptionalParameter_NullDefault_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/LambdaParameterParsingTests.cs:2485
        // "Func<string, string> func0 = (string x = null) => x;" (a `= null` default value). Adapted
        // to a method parameter.
        AssertParsesV4("class C { void M(string name = null) { } }");
    }

    // === VERSION-PURITY: optional parameters are a C# 4.0 feature (reject at v3). ===

    [TestMethod]
    public void OptionalParameter_RejectedAtV3()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParserErrorMessageTests.cs:6232
        // OptionalParameterBeforeCSharp4 — "void M(int x = 1) { }" under TestOptions.Regular3 ->
        // CS8024 "Feature 'optional parameter' is not available in C# 3." at the `=` token. At v3 the
        // Cs4 Parameter alternative is absent, so the Cs1 Parameter matches only `int x`, leaving
        // `= 5` -> the ParameterList/Method fails.
        AssertFailsV3("class C { void M(int x = 5) { } }");
    }

    // === NEGATIVE (version 4, malformed): must REJECT at v4. ===

    [TestMethod]
    public void OptionalParameter_MissingDefaultValue_Rejected()
        => AssertFailsV4("class C { void M(int x =) { } }");

    [TestMethod]
    public void OptionalParameter_MissingName_Rejected()
        => AssertFailsV4("class C { void M(int = 5) { } }");

    // === helpers ===

    private static void AssertParsesV4(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(4), input);

    private static void AssertFailsV4(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(4), input);

    private static void AssertFailsV3(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(3), input);

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
