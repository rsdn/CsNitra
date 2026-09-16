using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.2.1 — C# 3.0 lambda expressions (Cs3.grammar). Positives use CreateParser(3) (Cs1+Cs2+Cs3
// merged); version-purity negatives use CreateParser(2) (Cs1+Cs2, no CS3). A lambda is a Primary
// (expression): [params] "=>" (Expression | Block). The "=>" is a single literal (no new terminal);
// the fat arrow is disambiguated from a plain identifier / parenthesized expression by longest-match.
// See docs/CSharpParserPlan-progressT3.2.1.md for the hand-traces and the => tokenization finding.
[TestClass]
public class Cs3LambdaTests
{
    // === POSITIVE (version 3): lambda expressions. ===

    [TestMethod]
    public void Lambda_SingleUntypedParam_ExpressionBody_Succeeds()
        => AssertParsesV3("class C { void M() { Run(x => x + 1); } }");

    [TestMethod]
    public void Lambda_NoParams_ExpressionBody_Succeeds()
        => AssertParsesV3("class C { void M() { var f = () => 5; } }");

    [TestMethod]
    public void Lambda_TypedParam_ExpressionBody_Succeeds()
        => AssertParsesV3("class C { void M() { Run((int x) => x); } }");

    [TestMethod]
    public void Lambda_MultipleUntypedParams_ExpressionBody_Succeeds()
        => AssertParsesV3("class C { void M() { Run((x, y) => x + y); } }");

    [TestMethod]
    public void Lambda_SingleUntypedParam_BlockBody_Succeeds()
        => AssertParsesV3("class C { void M() { Run(x => { return x; }); } }");

    [TestMethod]
    public void Lambda_MultipleLambdasAsArguments_Succeeds()
        => AssertParsesV3("class C { void M() { N(x => x + 1, y => y + 1); } }");

    [TestMethod]
    public void Lambda_NestedLambdas_Succeeds()
        => AssertParsesV3("class C { void M() { Run(x => Run(y => x + y)); } }");

    [TestMethod]
    public void Lambda_ComplexExpressionBody_Succeeds()
        => AssertParsesV3("class C { void M() { Run(x => x > 0 ? x : -x); } }");

    // === POSITIVE (version 3): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void Roslyn_SimpleLambda_ExpressionBody_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:2039
        // TestSimpleLambda — "a => b" (SimpleLambdaExpression, expression body).
        AssertParsesV3("class C { void M() { Run(a => b); } }");
    }

    [TestMethod]
    public void Roslyn_SimpleLambda_BlockBody_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:2075
        // TestSimpleLambdaWithBlock — "a => { }" (SimpleLambdaExpression, block body).
        AssertParsesV3("class C { void M() { Run(a => { }); } }");
    }

    [TestMethod]
    public void Roslyn_NoParameters_ExpressionBody_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:2095
        // TestLambdaWithNoParameters — "() => b" (ParenthesizedLambdaExpression, empty list).
        AssertParsesV3("class C { void M() { var f = () => b; } }");
    }

    [TestMethod]
    public void Roslyn_NoParameters_BlockBody_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:2135
        // TestLambdaWithNoParametersAndBlock — "() => { }".
        AssertParsesV3("class C { void M() { var f = () => { }; } }");
    }

    [TestMethod]
    public void Roslyn_OneUntypedParameter_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:2157
        // TestLambdaWithOneParameter — "(a) => b" (untyped parameter).
        AssertParsesV3("class C { void M() { Run((a) => b); } }");
    }

    [TestMethod]
    public void Roslyn_TwoUntypedParameters_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:2181
        // TestLambdaWithTwoParameters — "(a, a2) => b".
        AssertParsesV3("class C { void M() { Run((a, a2) => b); } }");
    }

    [TestMethod]
    public void Roslyn_OneTypedParameter_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:2208
        // TestLambdaWithOneTypedParameter — "(T a) => b" (typed parameter).
        AssertParsesV3("class C { void M() { Run((T a) => b); } }");
    }

    // === VERSION-PURITY (version 2, no CS3): lambdas must REJECT at v2. ===

    [TestMethod]
    public void Lambda_SingleUntypedParam_RejectedAtV2()
        => AssertFailsV2("class C { void M() { Run(x => x + 1); } }");

    [TestMethod]
    public void Lambda_NoParams_RejectedAtV2()
        => AssertFailsV2("class C { void M() { var f = () => 5; } }");

    // === NEGATIVE (version 3, malformed): must REJECT at v3. ===

    [TestMethod]
    public void Lambda_MissingBody_Rejected()
        => AssertFailsV3("class C { void M() { var f = x => ; } }");

    [TestMethod]
    public void Lambda_MissingParams_Rejected()
        => AssertFailsV3("class C { void M() { var f = => x; } }");

    [TestMethod]
    public void Lambda_UnclosedParamList_Rejected()
        => AssertFailsV3("class C { void M() { Run((x => x + 1); } }");

    [TestMethod]
    public void Lambda_SplitArrow_Rejected()
    {
        // "=>" is a single literal; the split "= >" does not match the arrow, so `x = > x` is not a
        // lambda (the Assign RHS "> x" is not an expression) and the declaration fails.
        AssertFailsV3("class C { void M() { var f = x = > x; } }");
    }

    // === helpers ===

    private static void AssertParsesV3(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(3), input);

    private static void AssertFailsV3(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(3), input);

    private static void AssertFailsV2(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(2), input);

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
