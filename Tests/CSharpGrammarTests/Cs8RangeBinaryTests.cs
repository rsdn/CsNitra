using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.8.3b — C# 8.0 range operator `..` (BINARY form only: `left .. right`, both operands present).
// Positives use CreateParser(8) (Cs1+...+Cs8 merged); version-purity negatives use CreateParser(7)
// (Cs1+...+Cs7, no CS8). The binary `..` is a TDOPP POSTFIX on Expression at the NEW `Range` precedence
// level (Cs1.grammar, between Unary and Multiplicative): `RangeBinary = Expression : Range ".."
// Expression : Range` (Cs8.grammar). `..` is a 2-char LITERAL (a StartsWith match), distinct from the
// 1-char `.` literal of `MemberAccess = "." Identifier` (Cs1.grammar:731) — the member-access needs an
// Identifier after the single `.`, so a bare `..` never matches it (mutually exclusive). See
// docs/CSharpParserPlan-progressT3.8.3b.md for the hand-traces. The end-only (`.. 2`) and start-only
// (`2 ..`) forms are separate sub-points (T3.8.3c / T3.8.3d, out of scope here).
[TestClass]
public class Cs8RangeBinaryTests
{
    // === POSITIVE (version 8): binary range with LITERAL operands. ===
    // `1 .. 2` — left operand `DecInt`=`1` (the space after `1` stops `DecimalRealLiteral` from matching
    // `1.`); the RangeBinary POSTFIX then matches `..` + `Expression : Range`=`2`.

    [TestMethod]
    public void RangeBinary_Literals_Succeeds()
        => AssertParsesV8("class C { void M() { var r = 1 .. 2; } }");

    // === POSITIVE (version 8): binary range with VARIABLE operands. ===
    // `a .. b` — left operand `IdentifierName`=`a`; the RangeBinary POSTFIX matches `..` + `b`.

    [TestMethod]
    public void RangeBinary_Variables_Succeeds()
        => AssertParsesV8("class C { void M(int a, int b) { var r = a .. b; } }");

    // === POSITIVE (version 8): binary range in a return. ===

    [TestMethod]
    public void RangeBinary_InReturn_Succeeds()
        => AssertParsesV8("class C { object M() { return 1 .. 2; } }");

    // === POSITIVE (version 8): Roslyn-derived, adapted to the spaced binary form. ===

    [TestMethod]
    public void RangeBinary_Roslyn_Binary_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:5318
        // (RangeExpression_Binary) — `1..2` -> RangeExpression(NumericLiteral(1), DotDotToken,
        // NumericLiteral(2)). Adapted to the spaced form `5 .. 9` as a local initializer (the no-space
        // form `1..2` is out of scope: the `Real`/`1.` literal trap, see the progress doc).
        AssertParsesV8("class C { void M() { var r = 5 .. 9; } }");
    }

    [TestMethod]
    public void RangeBinary_AsArgument_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:5318
        // (RangeExpression_Binary) — a range expression used as a method ARGUMENT: the Cs4 Argument
        // operand is `Expression : Comma` (minPrecedence = Comma bp 1); RangeBinary (Range bp, higher)
        // applies, so `M(1 .. 2)` parses the range as the argument (it stops before `)` — no `,` to
        // absorb). Adapted to the spaced form.
        AssertParsesV8("class C { void M(object r) { } void N() { M(1 .. 2); } }");
    }

    [TestMethod]
    public void RangeBinary_TypedLocal_Succeeds()
    {
        // A binary range as the initializer of a TYPED local declaration (`object r = 1 .. 2;`): the
        // Cs1 LocalVariableDeclaration (Type VariableDeclarator ";") whose VariableDeclarator
        // initializer is the `1 .. 2` range expression.
        AssertParsesV8("class C { void M() { object r = 1 .. 2; } }");
    }

    // === POSITIVE (version 1 and 7, no-regression): the `.` MEMBER-ACCESS operator (one dot). ===
    // `x.y` — the `MemberAccess = "." Identifier` POSTFIXOP (Cs1.grammar:731) is UNCHANGED. The
    // RangeBinary POSTFIX needs TWO dots (`..`); `x.y` has ONE dot (followed by the identifier `y`), so
    // `MemberAccess` matches and the range postfix does not apply. No regression at v1-v7 (and v8).

    [TestMethod]
    public void MemberAccess_ParsesAtV1()
        => AssertParsesV1("class C { void M(object x) { var y = x.y; } }");

    [TestMethod]
    public void MemberAccess_ParsesAtV7()
        => AssertParsesV7("class C { void M(object x) { var y = x.y; } }");

    // === NEGATIVE (version 7, version-purity): the binary `..` is not available at v7. ===
    // At v7 the RangeBinary POSTFIX is ABSENT; the left operand `1` is parsed, then no postfix consumes
    // `..` (MemberAccess fails: the char after the first `.` is `.`, not an identifier), so the
    // statement sees `..` where `;` is expected -> REJECTS. At v8 it PARSES.

    [TestMethod]
    public void RangeBinary_RejectedAtV7()
        => AssertFailsV7("class C { void M() { var r = 1 .. 2; } }");

    // === POSITIVE (version 8, T3.8.3d): the start-only `1 ..` form now parses. ===
    // T3.8.3b originally expected `1 ..` to REJECT (the start-only form was "out of scope, T3.8.3d").
    // T3.8.3d added the UNARY POSTFIX `..` (start-only form, `RangePostfix = Expression : Range ".."`,
    // Cs8.grammar): the left operand `1` is parsed, then the `RangePostfix` POSTFIX matches the bare
    // `..` (the `RangeBinary` POSTFIX fails — no right operand after the `..`) -> `1 ..` parses. The
    // start-only form is now VALID at v8 (Roslyn tryExpandExpression, LanguageParser.cs:11617-11625,
    // right operand null when CanStartExpression() is false). See Cs8RangePostfixTests and
    // docs/CSharpParserPlan-progressT3.8.3d.md.

    [TestMethod]
    public void RangePostfix_StartOnly_ParsesAtV8()
        => AssertParsesV8("class C { void M() { var r = 1 .. ; } }");

    // === NEGATIVE (version 7, version-purity): the start-only `..` is not available at v7. ===
    // At v7 the RangePostfix POSTFIX is ABSENT; the left operand `1` is parsed, then no postfix consumes
    // the trailing `..` (MemberAccess fails: the char after the first `.` is `.`, not an identifier), so
    // the statement sees `..` where `;` is expected -> REJECTS. At v8 it PARSES.

    [TestMethod]
    public void RangePostfix_StartOnly_RejectedAtV7()
        => AssertFailsV7("class C { void M() { var r = 1 .. ; } }");

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
