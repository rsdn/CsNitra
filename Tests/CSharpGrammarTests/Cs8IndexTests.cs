using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.8.3a — C# 8.0 index-from-end `^` prefix operator (Cs8.grammar). Positives use CreateParser(8)
// (Cs1+...+Cs8 merged); version-purity negatives use CreateParser(7) (Cs1+...+Cs7, no CS8). See
// docs/CSharpParserPlan-progressT3.8.3a.md for the hand-traces. The index-from-end `^` is a TDOPP
// PREFIX on Expression at the Unary level: `IndexExpr = "^" Expression : Unary` (the SAME shape as the
// Cs5 `await` prefix and the Cs7 `ref`/`throw` prefixes). It is tried at the START of an expression, so
// it is MUTUALLY EXCLUSIVE with the `^` BITWISE-XOR (the Cs1 `BitXor` POSTFIX, `Expression "^"
// Expression : LogicalXor`), which is applied AFTER a left operand. `^` is a symbol (not a word
// keyword), so no Primary/Expression alternative starts with it. The `..` range operator is T3.8.3b
// (out of scope here).
[TestClass]
public class Cs8IndexTests
{
    // === POSITIVE (version 8): basic index-from-end. ===
    // `x[^1]` — the `Indexer` postfix (`"[" (Expression; ",")* "]"`, Cs1.grammar:732) parses an inner
    // Expression that starts with `^`; the `IndexExpr` prefix (`"^" Expression : Unary`) matches `^1`.
    // Roslyn: GetPrefixUnaryExpression (SyntaxKindFacts.cs:436-437, CaretToken -> IndexExpression).

    [TestMethod]
    public void IndexFromEnd_Basic_Succeeds()
        => AssertParsesV8("class C { void M(string x) { var y = x[^1]; } }");

    // === POSITIVE (version 8): index-from-end with a VARIABLE operand. ===
    // The operand is `Expression : Unary` (minPrecedence 15), so a bare identifier `i` is a valid
    // operand: `x[^i]` = index `i` from the end.

    [TestMethod]
    public void IndexFromEnd_Variable_Succeeds()
        => AssertParsesV8("class C { void M(string x, int i) { var y = x[^i]; } }");

    // === POSITIVE (version 8): index-from-end in a return. ===

    [TestMethod]
    public void IndexFromEnd_InReturn_Succeeds()
        => AssertParsesV8("class C { char M(string x) { return x[^1]; } }");

    // === POSITIVE (version 8): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void IndexFromEnd_Literal_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:5280
        // (IndexExpression) — `^1` -> IndexExpression(CaretToken, NumericLiteralExpression(1)). Adapted
        // to a full declaration as an indexer argument (the `^2` literal form covers a different index
        // value than the basic test).
        AssertParsesV8("class C { void M(string x) { var y = x[^2]; } }");
    }

    [TestMethod]
    public void IndexFromEnd_TwoInBinary_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:5337
        // (RangeExpression_Binary_WithIndexes) — `^5..^3`, where `^5` and `^3` are index-from-end
        // expressions that are the BOUNDS of a `..` range. The `..` range operator is T3.8.3b (out of
        // scope here), so only the two `^` index-from-end expressions are in scope: adapted to two
        // separate indexers combined with `+` (exercising the `^` prefix in a binary context; the
        // `^` prefix binds tighter than `+`, so `x[^5] + x[^3]` = `(x[^5]) + (x[^3])`).
        AssertParsesV8("class C { void M(string x) { var y = x[^5] + x[^3]; } }");
    }

    [TestMethod]
    public void IndexFromEnd_LocalDeclaration_Roslyn_Succeeds()
    {
        // An index-from-end expression as the initializer of a local variable declaration with an
        // explicit type (`char c = x[^1];`): the Cs1 LocalVariableDeclaration (Type VariableDeclarator
        // ";" ) whose VariableDeclarator initializer is the `x[^1]` expression.
        AssertParsesV8("class C { char M(string x) { char c = x[^1]; return c; } }");
    }

    // === POSITIVE (version 1 and 7, no-regression): the `^` BITWISE-XOR operator. ===
    // The `^` BITWISE-XOR (the Cs1 `BitXor` POSTFIX, `Expression "^" Expression : LogicalXor`,
    // Cs1.grammar:667) is a DIFFERENT TDOPP phase from the `^` PREFIX (IndexExpr): the postfix is
    // applied AFTER a left operand, the prefix is tried at the START of an expression. So `x ^ y`
    // (bitwise-XOR) is UNCHANGED at every version — the `^` prefix is absent at v1-v7 and at v8 it only
    // applies at the START of an expression (so it does not interfere with `x ^ y`). No regression.

    [TestMethod]
    public void BitwiseXor_ParsesAtV1()
        => AssertParsesV1("class C { int M(int x, int y) { return x ^ y; } }");

    [TestMethod]
    public void BitwiseXor_ParsesAtV7()
        => AssertParsesV7("class C { int M(int x, int y) { return x ^ y; } }");

    // === NEGATIVE (version 7, version-purity): the index-from-end is not available at v7. ===
    // At v7 the IndexExpr prefix is ABSENT; the indexer's inner Expression starts with `^`, PrimaryExpr
    // fails (no Primary matches the `^` symbol), and no other prefix matches `^` (the BitXor POSTFIX
    // requires a left operand and cannot start the expression) -> the inner Expression fails -> the
    // Indexer fails -> REJECTS. At v8 it PARSES.

    [TestMethod]
    public void IndexFromEnd_RejectedAtV7()
        => AssertFailsV7("class C { void M(string x) { var y = x[^1]; } }");

    // === NEGATIVE (version 8, malformed): a missing operand. ===
    // `x[^]` — the `IndexExpr` prefix (`"^" Expression : Unary`) requires an `Expression : Unary` after
    // the `^`, but the next token is `]` (not an expression start) -> the prefix fails -> the inner
    // Expression fails -> the Indexer fails -> REJECTS. (Roslyn recovers this with a missing-operand
    // error; this grammar rejects it.)

    [TestMethod]
    public void IndexFromEnd_MissingOperand_Rejected()
        => AssertFailsV8("class C { void M(string x) { var y = x[^]; } }");

    // === helpers ===

    private static void AssertParsesV8(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(8), input);

    private static void AssertFailsV8(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(8), input);

    private static void AssertParsesV7(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(7), input);

    private static void AssertFailsV7(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(7), input);

    private static void AssertParsesV1(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(1), input);

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
