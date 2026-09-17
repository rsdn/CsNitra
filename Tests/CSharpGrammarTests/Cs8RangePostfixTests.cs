using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.8.3d — C# 8.0 range operator `..` (UNARY POSTFIX form only: `left ..`, no right operand, the
// start-only form). Positives use CreateParser(8) (Cs1+...+Cs8 merged); version-purity negatives use
// CreateParser(7) (Cs1+...+Cs7, no CS8). The start-only `..` is a TDOPP POSTFIX on Expression at the
// `Range` precedence level: `RangePostfix = Expression : Range ".."` (Cs8.grammar). Roslyn
// tryExpandExpression (LanguageParser.cs:11617-11625): `RangeExpression(leftOperand, ..,
// CanStartExpression() ? ParseSubExpression(Precedence.Range) : null)` — when nothing expression-start
// follows the `..`, the right operand is null (the start-only form). It is a POSTFIX (a left operand is
// present), so it is MUTUALLY EXCLUSIVE with the T3.8.3c prefix `RangePrefix` (`.. 2`, no left operand,
// tried at the START of an expression). It coexists with the T3.8.3b BINARY `RangeBinary` POSTFIX via
// longest-match: for `2 .. 3` the binary form consumes `2 .. 3` (longer) and wins; for `2 ..` (nothing
// after) the binary form FAILS (no right operand) and the `RangePostfix` matches the bare `..`. `..` is a
// 2-char LITERAL, distinct from the 1-char `.` literal of `MemberAccess = "." Identifier`
// (Cs1.grammar:731) — the member-access needs an Identifier after the single `.`, so a bare `..` never
// matches it (no regression). See docs/CSharpParserPlan-progressT3.8.3d.md for the hand-traces.
[TestClass]
public class Cs8RangePostfixTests
{
    // === POSITIVE (version 8): start-only range with a LITERAL left operand. ===
    // `2 ..` — the left operand `DecInt`=`2` is parsed (PrimaryExpr); the `RangePostfix` POSTFIX
    // (`Expression : Range ".."`) matches the bare `..` (no right operand). The `RangeBinary` POSTFIX
    // FAILS (the next token is `;`, not an expression start, so its `Expression : Range` right operand
    // fails) -> the shorter `RangePostfix` wins. Roslyn tryExpandExpression (LanguageParser.cs:11617-
    // 11625): RangeExpression(NumericLiteral(2), DotDotToken, null).

    [TestMethod]
    public void RangePostfix_Literal_Succeeds()
        => AssertParsesV8("class C { void M() { var r = 2 .. ; } }");

    // === POSITIVE (version 8): start-only range with a VARIABLE left operand. ===
    // `a ..` — the left operand `IdentifierName`=`a`; the `RangePostfix` POSTFIX matches the bare `..`.

    [TestMethod]
    public void RangePostfix_Variable_Succeeds()
        => AssertParsesV8("class C { void M(int a) { var r = a .. ; } }");

    // === POSITIVE (version 8): start-only range in a return. ===

    [TestMethod]
    public void RangePostfix_InReturn_Succeeds()
        => AssertParsesV8("class C { object M() { return 2 .. ; } }");

    // === POSITIVE (version 8): start-only range with an INDEXER-argument-shaped left operand. ===
    // `x ..` where the left operand is a member access `x` — the left operand is `IdentifierName`=`x`;
    // the `RangePostfix` matches the trailing `..`. (The `.` member-access POSTFIXOP needs an Identifier
    // after a single `.`; the trailing `..` has a `.` as its second char, so `MemberAccess` does NOT fire
    // and the `..` is left for the `RangePostfix` TDOPP postfix.)

    [TestMethod]
    public void RangePostfix_MemberLeftOperand_Succeeds()
        => AssertParsesV8("class C { void M(object x) { var r = x .. ; } }");

    // === POSITIVE (version 8, disambiguation): the BINARY `1 .. 2` still parses (longer wins). ===
    // For `1 .. 2` BOTH the `RangeBinary` POSTFIX (consumes `1 .. 2`) and the `RangePostfix` POSTFIX
    // (consumes `1 ..`) match; longest-match-wins (Parser.cs:386) keeps the BINARY form. Adding the
    // `RangePostfix` does NOT steal the binary form.

    [TestMethod]
    public void RangeBinary_ParsesAtV8_Succeeds()
        => AssertParsesV8("class C { void M() { var r = 1 .. 2; } }");

    // === POSITIVE (version 8, disambiguation): `2 .. 3` (both operands) is the BINARY form. ===
    // The binary form consumes the whole `2 .. 3` (longer than the `2 ..` the postfix form would
    // consume) -> it wins.

    [TestMethod]
    public void RangeBinary_BothOperands_ParsesAtV8_Succeeds()
        => AssertParsesV8("class C { void M() { var r = 2 .. 3; } }");

    // === POSITIVE (version 8, no-regression): the T3.8.3c PREFIX `.. 2` still parses. ===
    // `.. 2` starts with `..` (no left operand) -> the `RangePrefix` PREFIX (tried at the START of the
    // expression) matches; the `RangePostfix` POSTFIX requires a left operand and is never tried (there
    // is no primary to apply it to). Adding the postfix does not interfere with the prefix form.

    [TestMethod]
    public void RangePrefix_ParsesAtV8_Succeeds()
        => AssertParsesV8("class C { void M() { var r = .. 2; } }");

    // === POSITIVE (version 1 and 7, no-regression): the `.` MEMBER-ACCESS operator (one dot). ===
    // `x.y` — the `MemberAccess = "." Identifier` POSTFIXOP (Cs1.grammar:731) is UNCHANGED. The
    // `RangePostfix` POSTFIX needs TWO dots (`..`); `x.y` has ONE dot (followed by the identifier `y`),
    // so `MemberAccess` matches and the range postfix does not apply. No regression at v1-v7 (and v8).

    [TestMethod]
    public void MemberAccess_ParsesAtV1()
        => AssertParsesV1("class C { void M(object x) { var y = x.y; } }");

    [TestMethod]
    public void MemberAccess_ParsesAtV7()
        => AssertParsesV7("class C { void M(object x) { var y = x.y; } }");

    // === NEGATIVE (version 7, version-purity): the start-only `..` is not available at v7. ===
    // At v7 the RangePostfix POSTFIX is ABSENT; the left operand `2` is parsed, then no postfix consumes
    // the trailing `..` (MemberAccess fails: the char after the first `.` is `.`, not an identifier), so
    // the statement sees `..` where `;` is expected -> REJECTS. At v8 it PARSES.

    [TestMethod]
    public void RangePostfix_RejectedAtV7()
        => AssertFailsV7("class C { void M() { var r = 2 .. ; } }");

    // === NEGATIVE (version 8, malformed): a BARE `..` with no left AND no right operand is not the
    // start-only form (it has no left operand) — that is the T3.8.3c PREFIX missing-operand case. ===
    // `.. ;` — the `RangePrefix` PREFIX requires an `Expression : Range` after the `..` (the next token
    // is `;`, not an expression start) -> fails; no other prefix starts with `..`; the `RangePostfix`
    // POSTFIX requires a left operand (there is no primary at the start) -> the Expression fails ->
    // REJECTS.

    [TestMethod]
    public void Range_BareDotDotNoOperands_Rejected()
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
