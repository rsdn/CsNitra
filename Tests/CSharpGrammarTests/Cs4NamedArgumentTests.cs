using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.3.2 — C# 4.0 named arguments (Cs4.grammar). Positives use CreateParser(4) (Cs1+Cs2+Cs3+Cs4
// merged); version-purity negatives use CreateParser(3) (Cs1+Cs2+Cs3, no CS4).
//
// A named argument is `Identifier ":" Expression`; a positional argument is just `Expression`. The
// method-call argument list is `Invocation` (a named alternative of `PostfixOp`, Cs1.grammar:725),
// so Cs4 re-declares `PostfixOp` to append `NamedInvocation = "(" (Argument; ",")* ")"` with
// `Argument = NamedArgument | Expression`. At v3 the `NamedInvocation` alternative is absent, so
// `N(x: 5)` REJECTS (the Cs1 `Invocation` fails: `x: 5` is not an Expression); at v4 it parses.
// See docs/CSharpParserPlan-progressT3.3.2.md for the hand-traces.
[TestClass]
public class Cs4NamedArgumentTests
{
    // === POSITIVE (version 4): named arguments in a method call. ===

    [TestMethod]
    public void NamedArgument_Single_Succeeds()
        => AssertParsesV4("class C { void M() { N(x: 5); } }");

    [TestMethod]
    public void NamedArgument_Multiple_Succeeds()
        => AssertParsesV4("class C { void M() { N(x: 5, y: 6); } }");

    [TestMethod]
    public void NamedArgument_AnyOrder_Succeeds()
        => AssertParsesV4("class C { void M() { N(y: 6, x: 5); } }");

    [TestMethod]
    public void NamedArgument_PositionalBeforeNamed_Succeeds()
        => AssertParsesV4("class C { void M() { N(1, y: 6); } }");

    [TestMethod]
    public void NamedArgument_ComplexValue_Succeeds()
        => AssertParsesV4("class C { void M() { N(x: a + b); } }");

    [TestMethod]
    public void NamedArgument_ValueIsMethodCall_Succeeds()
        => AssertParsesV4("class C { void M() { N(x: M2()); } }");

    // === POSITIVE (version 4): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void NamedArgument_Call_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:1024
        // TestCallWithNamedArgument — "a(B: b)" (an InvocationExpression whose single argument has
        // NameColon "B" and Expression "b"). Adapted to a full declaration.
        AssertParsesV4("class C { void M() { N(B: b); } }");
    }

    // === VERSION-PURITY: named arguments are a C# 4.0 feature (reject at v3). ===

    [TestMethod]
    public void NamedArgument_RejectedAtV3()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParserErrorMessageTests.cs:6140
        // NamedArgumentBeforeCSharp4 — "M(y:2)" under TestOptions.Regular3 -> CS8024 "Feature
        // 'named argument' is not available in C# 3." At v3 the NamedInvocation alternative is
        // absent, so the Cs1 Invocation fails (`x: 5` is not an Expression) and the call rejects.
        AssertFailsV3("class C { void M() { N(x: 5); } }");
    }

    // === NEGATIVE (version 4, malformed): must REJECT at v4. ===

    [TestMethod]
    public void NamedArgument_MissingColon_Rejected()
        => AssertFailsV4("class C { void M() { N(x 5); } }");

    [TestMethod]
    public void NamedArgument_MissingName_Rejected()
        => AssertFailsV4("class C { void M() { N(: 5); } }");

    [TestMethod]
    public void NamedArgument_MissingValue_Rejected()
        => AssertFailsV4("class C { void M() { N(x:); } }");

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
