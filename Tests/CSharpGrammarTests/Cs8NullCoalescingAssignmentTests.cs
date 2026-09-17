using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.8.4 — C# 8.0 null-coalescing assignment `??=` operator.
// Positives use CreateParser(8) (Cs1+...+Cs8 merged); version-purity negatives use CreateParser(7)
// (Cs1+...+Cs7, no CS8). `??=` is a TDOPP POSTFIX (binary) operator on Expression at the EXISTING
// `Assignment` precedence level (the loosest expression level, Cs1.grammar:629) — the SAME level as the
// simple assignment `Assign = Expression "=" Expression : Assignment, right` (Cs1.grammar:675):
// `CoalesceAssign = Expression "??=" Expression : Assignment, right` (Cs8.grammar). `??=` is a 3-char
// grammar LITERAL (a StartsWith match, Rules.cs:76-77) — NOT a terminal (the same mechanism as the `..`
// literal, T3.8.3b). Roslyn: GetPrecedence (LanguageParser.cs:11245-11246) -> Precedence.Assignment;
// IsRightAssociative (LanguageParser.cs:11189) -> true (hence `, right`). See
// docs/CSharpParserPlan-progressT3.8.4.md.
[TestClass]
public class Cs8NullCoalescingAssignmentTests
{
    // === POSITIVE (version 8): basic `a ??= b` (both identifiers). ===
    // `a ??= b` — left operand `IdentifierName`=`a`; the CoalesceAssign POSTFIX matches `??=` +
    // `Expression : Assignment`=`b`. Roslyn NullCoalescingAssignmentExpression (ExpressionParsingTests.cs:
    // 5083-5099) — `a ??= b` -> CoalesceAssignmentExpression(IdentifierName(a), ??=, IdentifierName(b)).

    [TestMethod]
    public void CoalesceAssign_Variables_Succeeds()
        => AssertParsesV8("class C { void M(object a, object b) { a ??= b; } }");

    // === POSITIVE (version 8): parenthesized LHS `(a) ??= b`. ===
    // Roslyn NullCoalescingAssignmentExpressionParenthesized (ExpressionParsingTests.cs:5101-5114) —
    // `(a) ??= b` -> CoalesceAssignmentExpression(ParenthesizedExpression(a), ??=, IdentifierName(b)).
    // The left operand is the `Parens = "(" Expression ")"` Primary (Cs1.grammar:710); the CoalesceAssign
    // POSTFIX then matches `??=` + `b`.

    [TestMethod]
    public void CoalesceAssign_ParenthesizedLhs_Succeeds()
        => AssertParsesV8("class C { void M(object a, object b) { (a) ??= b; } }");

    // === POSITIVE (version 8): the RHS is a FULL expression (absorbs a tighter operator). ===
    // `a ??= b + c` — the CoalesceAssign RHS is `Expression : Assignment` (minPrecedence = Assignment bp
    // 4), so the `+` (Additive bp 13, higher) applies to the RHS: `a ??= (b + c)`. Assignment is the
    // LOOSEST expression level (above only the Comma), so the RHS absorbs every tighter binary operator.

    [TestMethod]
    public void CoalesceAssign_RhsAdditive_Succeeds()
        => AssertParsesV8("class C { void M(object a, object b, object c) { a ??= b + c; } }");

    // === POSITIVE (version 8): right-associative chaining `a ??= b ??= c`. ===
    // `??=` is RIGHT-associative (Roslyn IsRightAssociative, LanguageParser.cs:11189), so `a ??= b ??= c`
    // = `a ??= (b ??= c)`. The `, right` flag on the rule (identical to `Assign`) makes the RHS
    // `Expression : Assignment` re-absorb a same-level `??=`.

    [TestMethod]
    public void CoalesceAssign_ChainedRightAssoc_Succeeds()
        => AssertParsesV8("class C { void M(object a, object b, object c) { a ??= b ??= c; } }");

    // === POSITIVE (version 1 and 7, no-regression): the SIMPLE assignment `a = b` (Cs1 `Assign`). ===
    // `a = b` — the `Assign = Expression "=" Expression : Assignment, right` POSTFIX (Cs1.grammar:675) is
    // UNCHANGED. The `??=` literal needs THREE chars (`?` `?` `=`); a bare `=` is one char, so the
    // CoalesceAssign POSTFIX (Cs8-only) does not interfere and `Assign` matches. No regression at v1-v8.

    [TestMethod]
    public void SimpleAssign_ParsesAtV1()
        => AssertParsesV1("class C { void M(object a, object b) { a = b; } }");

    [TestMethod]
    public void SimpleAssign_ParsesAtV7()
        => AssertParsesV7("class C { void M(object a, object b) { a = b; } }");

    // === NEGATIVE (version 7, version-purity): `??=` is not available at v7. ===
    // At v7 the CoalesceAssign POSTFIX is ABSENT; the left operand `a` is parsed, then no postfix consumes
    // `??=` (`Conditional` fails on the middle operand — `?` cannot start an expression; `Assign` needs a
    // bare `=`, not `??=`; no other postfix matches `??=`), so the statement sees `??=` where `;` is
    // expected -> REJECTS. At v8 it PARSES.

    [TestMethod]
    public void CoalesceAssign_RejectedAtV7()
        => AssertFailsV7("class C { void M(object a, object b) { a ??= b; } }");

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
