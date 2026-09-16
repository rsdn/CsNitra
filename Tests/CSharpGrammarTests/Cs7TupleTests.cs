using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.6.1 — C# 7.0 tuples (Cs7.grammar). Positives use CreateParser(7) (Cs1+...+Cs7 merged);
// version-purity negatives use CreateParser(6) (Cs1+...+Cs6, no CS7). See
// docs/CSharpParserPlan-progressT3.6.1.md for the hand-traces and the documented deviations:
//   D1 — the UNNAMED tuple literal `(1, "a")` PARSES at v6 (as a parenthesized comma expression
//        via the Cs1 Parens + Comma operator), so its version-purity negative is BLOCKED;
//   D2 — the deconstruction assignment `(x, y) = t;` is an expression statement (not a separate rule);
//   D3 — the deconstruction variable list is a FLAT list (nested deconstruction is out of scope).
[TestClass]
public class Cs7TupleTests
{
    // === POSITIVE (version 7): tuple types. ===

    [TestMethod]
    public void TupleType_Unnamed_Succeeds()
        => AssertParsesV7("class C { (int, string) M() { return (1, \"a\"); } }");

    [TestMethod]
    public void TupleType_Named_Succeeds()
        => AssertParsesV7("class C { (int a, string b) M() { return (a: 1, b: \"b\"); } }");

    [TestMethod]
    public void TupleType_Nested_Succeeds()
        => AssertParsesV7("class C { ((int, string), bool) M() { return ((1, \"a\"), true); } }");

    [TestMethod]
    public void TupleType_ThreeElements_Succeeds()
        => AssertParsesV7("class C { (int, string, bool) M() { return (1, \"a\", true); } }");

    [TestMethod]
    public void TupleType_AsField_Succeeds()
        => AssertParsesV7("class C { (int, string) F; }");

    [TestMethod]
    public void TupleType_AsParameter_Succeeds()
        => AssertParsesV7("class C { void M((int, string) t) { } }");

    // === POSITIVE (version 7): tuple literals. ===

    [TestMethod]
    public void TupleLiteral_Unnamed_Succeeds()
        => AssertParsesV7("class C { void M() { var t = (1, \"a\"); } }");

    [TestMethod]
    public void TupleLiteral_Named_Succeeds()
        => AssertParsesV7("class C { void M() { var t = (a: 1, b: \"b\"); } }");

    [TestMethod]
    public void TupleLiteral_Nested_Succeeds()
        => AssertParsesV7("class C { void M() { var t = ((1, \"a\"), true); } }");

    [TestMethod]
    public void TupleLiteral_ThreeElements_Succeeds()
        => AssertParsesV7("class C { void M() { var t = (1, \"a\", true); } }");

    // === POSITIVE (version 7): deconstruction. ===

    [TestMethod]
    public void Deconstruction_Declaration_Succeeds()
        => AssertParsesV7("class C { void M() { var (x, y) = t; } }");

    // The deconstruction assignment is an assignment EXPRESSION (Roslyn ParseExpressionContinued,
    // LanguageParser.cs:11532) whose LHS is a parenthesized variable designation. In this grammar it
    // is parsed via the Cs1 ExpressionStatement (Deviation D2) — no separate rule is required.
    [TestMethod]
    public void Deconstruction_Assignment_Succeeds()
        => AssertParsesV7("class C { void M() { (x, y) = t; } }");

    [TestMethod]
    public void Deconstruction_ThreeElements_Succeeds()
        => AssertParsesV7("class C { void M() { var (x, y, z) = t; } }");

    // === POSITIVE (version 7): Item1/Item2 access (regular member access, Cs1 MemberAccess). ===

    [TestMethod]
    public void Item1_Access_Succeeds()
        => AssertParsesV7("class C { void M() { var x = t.Item1; } }");

    [TestMethod]
    public void Item2_Access_Succeeds()
        => AssertParsesV7("class C { void M() { var x = t.Item2; } }");

    // === POSITIVE (version 7): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void TupleExpression_TwoArguments_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:2260
        // (TestTupleWithTwoArguments) — `(a, a2)` is a 2-element TupleExpression. Adapted to a local.
        AssertParsesV7("class C { void M() { var t = (a, a2); } }");
    }

    [TestMethod]
    public void TupleExpression_TwoNamedArguments_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:2280
        // (TestTupleWithTwoNamedArguments) — `(arg1: (a, a2), arg2: a2)` is a 2-element NAMED
        // TupleExpression (the first element is a nested tuple). Adapted to a local.
        AssertParsesV7("class C { void M() { var t = (arg1: (a, a2), arg2: a2); } }");
    }

    [TestMethod]
    public void LocalDeclaration_TupleType_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/StatementParsingTests.cs:238
        // (TestLocalDeclarationStatementWithTuple) — `(int, int) a;` is a LocalDeclarationStatement
        // whose type is a TupleType. Adapted to a method body.
        AssertParsesV7("class C { void M() { (int, int) a; } }");
    }

    [TestMethod]
    public void TupleType_TwoItemAsTypeArgument_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/TypeArgumentListParsingTests.cs:531
        // (TestTwoItemTupleType) — a 2-item tuple type used as a type argument. Adapted with a
        // generic local (`new Dictionary<(int, string), int>()`).
        AssertParsesV7("class C { void M() { var d = new Dictionary<(int, string), int>(); } }");
    }

    // === POSITIVE (version 6): a parenthesized expression `(1)` is valid at every version. ===
    // A single-element "tuple" is NOT a tuple type (Roslyn ERR_TupleTooFewElements) — `(int)` is not
    // a Type, but `(1)` is a parenthesized EXPRESSION (Cs1 Parens).

    [TestMethod]
    public void ParenthesizedExpression_ParsesAtV6()
        => AssertParsesV6("class C { void M() { var t = (1); } }");

    // === NEGATIVE (version 6, version-purity): the CS7 tuple constructs. ===

    [TestMethod]
    public void TupleType_RejectedAtV6()
        => AssertFailsV6("class C { (int, string) M() { return (1, \"a\"); } }");

    [TestMethod]
    public void DeconstructionDeclaration_RejectedAtV6()
        => AssertFailsV6("class C { void M() { var (x, y) = t; } }");

    // The NAMED tuple literal rejects at v6 (the Cs1 Parens fails on `a: 1`, which is not an
    // Expression; there is no TupleLiteral at v6). This is the clean v6-vs-v7 discriminator for the
    // tuple literal (the UNNAMED form is blocked — see below).
    [TestMethod]
    public void TupleLiteral_Named_RejectedAtV6()
        => AssertFailsV6("class C { void M() { var t = (a: 1, b: \"b\"); } }");

    // === DOCUMENT (version 6): the UNNAMED tuple literal PARSES at v6 (Deviation D1). ===
    // The task listed `var t = (1, "a");` as a v6 version-purity negative, but the Cs1
    // `Parens = "(" Expression ")"` (Cs1.grammar:707) matches `(1, "a")` as a parenthesized COMMA
    // EXPRESSION (the inner `Expression` absorbs the `,` via the `Comma` operator, bp 1, applicable
    // at minPrecedence 0) at the same length as the Cs7 `TupleLiteral`; the FIRST (Cs1 `Parens`)
    // wins the tie (Parser.cs:296-316). So the UNNAMED tuple literal PARSES at v6 (as a comma
    // expression) — its version-purity negative is BLOCKED without modifying Cs1/the engine.
    [TestMethod]
    public void TupleLiteral_Unnamed_ParsesAtV6()
        => AssertParsesV6("class C { void M() { var t = (1, \"a\"); } }");

    // === NEGATIVE (version 7, malformed). ===

    [TestMethod]
    public void TupleLiteral_TrailingComma_Rejected()
        => AssertFailsV7("class C { void M() { var t = (1,); } }");

    [TestMethod]
    public void TupleLiteral_LeadingComma_Rejected()
        => AssertFailsV7("class C { void M() { var t = (,1); } }");

    [TestMethod]
    public void TupleType_TrailingComma_Rejected()
        => AssertFailsV7("class C { (int, string,) M() { return (1, \"a\"); } }");

    [TestMethod]
    public void Deconstruction_TrailingComma_Rejected()
        => AssertFailsV7("class C { void M() { var (x,) = t; } }");

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
