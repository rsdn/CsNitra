using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.8.3c — C# 8.0 range operator `..` (UNARY PREFIX form only: `.. right`, no left operand).
// Positives use CreateParser(8) (Cs1+...+Cs8 merged); version-purity negatives use CreateParser(7)
// (Cs1+...+Cs7, no CS8). The prefix `..` is a TDOPP PREFIX on Expression at the `Range` precedence
// level: `RangePrefix = ".." Expression : Range` (Cs8.grammar). Roslyn parseUnaryOrPrimaryExpression
// (LanguageParser.cs:11478-11486): a `..` token with leftOperand: null and right operand
// ParseSubExpression(Precedence.Range) — a PREFIX unary operator (no associativity flag). It is tried
// at the START of an expression (no left operand), so it is MUTUALLY EXCLUSIVE with the T3.8.3b binary
// `RangeBinary` POSTFIX (a left operand is present): `.. 2` -> RangePrefix, `1 .. 2` -> PrimaryExpr `1`
// + RangeBinary postfix. `..` is a 2-char LITERAL, distinct from the 1-char `.` literal of
// `MemberAccess = "." Identifier` (Cs1.grammar:731) — the member-access needs an Identifier after the
// single `.`, so a bare `..` never matches it (no regression). See docs/CSharpParserPlan-progressT3.8.3c.md.
[TestClass]
public class Cs8RangePrefixTests
{
    // === POSITIVE (version 8): prefix range with a LITERAL operand. ===
    // `.. 2` — at the START of the initializer Expression, the `RangePrefix` prefix (`".." Expression :
    // Range`) matches `..` + `Expression : Range`=`2` (DecInt). Roslyn parseUnaryOrPrimaryExpression
    // (LanguageParser.cs:11478-11486): RangeExpression(leftOperand: null, DotDotToken, NumericLiteral(2)).

    [TestMethod]
    public void RangePrefix_Literal_Succeeds()
        => AssertParsesV8("class C { void M() { var r = .. 2; } }");

    // === POSITIVE (version 8): prefix range with a VARIABLE operand. ===
    // `.. a` — the operand is `Expression : Range` (minPrecedence = Range); a bare identifier `a` is a
    // valid operand.

    [TestMethod]
    public void RangePrefix_Variable_Succeeds()
        => AssertParsesV8("class C { void M(int a) { var r = .. a; } }");

    // === POSITIVE (version 8): prefix range in a return. ===

    [TestMethod]
    public void RangePrefix_InReturn_Succeeds()
        => AssertParsesV8("class C { object M() { return .. 2; } }");

    // === POSITIVE (version 8): prefix range with a UNARY-MINUS operand. ===
    // `.. -5` — the operand is `Expression : Range`; at the START of the operand the `UnaryMinus` prefix
    // (`"-" Expression : Unary`, Cs1.grammar:639) matches `-5` (Unary is tighter than Range, so it is
    // absorbed into the operand). Roslyn: the operand ParseSubExpression(Precedence.Range) absorbs the
    // unary `-` (Unary > Range). -> `.. (-5)`.

    [TestMethod]
    public void RangePrefix_UnaryMinusOperand_Succeeds()
        => AssertParsesV8("class C { void M() { var r = .. -5; } }");

    // === POSITIVE (version 8): prefix range binds TIGHTER than a binary operator. ===
    // `.. 2 + 3` — the operand is `Expression : Range`; `+` (Additive) is LOOSER than Range, so it is NOT
    // absorbed into the operand (the operand is just `2`). The `RangePrefix` yields `.. 2`, then the
    // Additive POSTFIX applies at the top level -> `(.. 2) + 3`. Roslyn: the operand
    // ParseSubExpression(Precedence.Range) stops at `+` (Additive < Range).

    [TestMethod]
    public void RangePrefix_BindsTighterThanAdditive_Succeeds()
        => AssertParsesV8("class C { void M() { var r = .. 2 + 3; } }");

    // === POSITIVE (version 8, no-regression): the T3.8.3b BINARY `..` still parses. ===
    // `1 .. 2` — the left operand `1` is parsed (PrimaryExpr); the `RangePrefix` prefix FAILS (the start
    // is `1`, not `..`); the `RangeBinary` POSTFIX then matches `..` + `2`. The prefix and the binary
    // postfix are in DIFFERENT TDOPP phases, so adding the prefix does not interfere with the binary form.

    [TestMethod]
    public void RangeBinary_ParsesAtV8_Succeeds()
        => AssertParsesV8("class C { void M() { var r = 1 .. 2; } }");

    // === POSITIVE (version 1 and 7, no-regression): the `.` MEMBER-ACCESS operator (one dot). ===
    // `x.y` — the `MemberAccess = "." Identifier` POSTFIXOP (Cs1.grammar:731) is UNCHANGED. The
    // `RangePrefix` prefix needs TWO dots (`..`) at the START of an expression; `x.y` has ONE dot (and it
    // is a POSTFIX after `x`), so `MemberAccess` matches and the prefix does not apply. No regression.

    [TestMethod]
    public void MemberAccess_ParsesAtV1()
        => AssertParsesV1("class C { void M(object x) { var y = x.y; } }");

    [TestMethod]
    public void MemberAccess_ParsesAtV7()
        => AssertParsesV7("class C { void M(object x) { var y = x.y; } }");

    // === NEGATIVE (version 7, version-purity): the prefix `..` is not available at v7. ===
    // At v7 the RangePrefix prefix is ABSENT; at the START of the initializer Expression, PrimaryExpr
    // FAILS (no Primary matches the `..` symbol) and no other prefix starts with `..` (the RangeBinary
    // POSTFIX requires a left operand and cannot start the expression) -> the Expression fails -> REJECTS.
    // At v8 it PARSES.

    [TestMethod]
    public void RangePrefix_RejectedAtV7()
        => AssertFailsV7("class C { void M() { var r = .. 2; } }");

    // === NEGATIVE (version 8, malformed): a MISSING operand. ===
    // `.. ;` — the `RangePrefix` prefix (`".." Expression : Range`) requires an `Expression : Range` after
    // the `..`, but the next token is `;` (not an expression start) -> the prefix fails -> the Expression
    // fails -> REJECTS. (The start-only form `2 ..` is T3.8.3d, out of scope.)

    [TestMethod]
    public void RangePrefix_MissingOperand_Rejected()
        => AssertFailsV8("class C { void M() { var r = .. ; } }");

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
