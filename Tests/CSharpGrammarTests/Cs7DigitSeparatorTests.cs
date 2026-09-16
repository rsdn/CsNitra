using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.6.5 — C# 7.0 digit separators + `throw` as expression (Cs7.grammar).
//   * Digit separators — `1_000_000` / `0xFF_FF` / `0b1_0` / `1_0.5` (underscores in numeric
//     literals). A TERMINAL change: new `[Regex]` terminals that REQUIRE at least one `_` (mutually
//     exclusive with the Cs1 no-separator terminals), wired into `Primary`/`Constant`.
//   * `throw` as expression — `c ? throw e : 5` (a `throw` + expression, NO semicolon). A `throw`
//     prefix operator on `Expression` (like the Cs7 `ref` / the Cs5 `await`). The Cs1 `ThrowStatement`
//     (`throw e;`) is UNCHANGED.
// Positives use CreateParser(7) (Cs1+...+Cs7 merged); version-purity negatives use CreateParser(6)
// (Cs1+...+Cs6, no CS7). See docs/CSharpParserPlan-progressT3.6.5.md for the hand-traces.
[TestClass]
public class Cs7DigitSeparatorTests
{
    // === POSITIVE (version 7): digit separators. ===
    // Each separated-literal terminal REQUIRES at least one `_`, so it is mutually exclusive with the
    // Cs1 no-separator terminal (longest-match, no tie). Roslyn Lexer.cs:801-842 (IDS_FeatureDigitSeparator).

    // Integer separator: `1_000_000` — SeparatedDecInt (length 9) beats DecInt (`1`, length 1).
    [TestMethod]
    public void IntegerSeparator_Succeeds()
        => AssertParsesV7("class C { int X = 1_000_000; }");

    // Float separator: `1_0.5` — the separator is in the integer part. SeparatedReal (length 5) is the
    // sole match (Real fails: `1` then `_`, not `.`; DecInt matches only `1`).
    [TestMethod]
    public void FloatSeparator_Succeeds()
        => AssertParsesV7("class C { double X = 1_0.5; }");

    // Hex separator: `0xFF_FF` — SeparatedHexInt (length 7) beats HexInt (`0xFF`, length 4).
    [TestMethod]
    public void HexSeparator_Succeeds()
        => AssertParsesV7("class C { int X = 0xFF_FF; }");

    // Binary separator: `0b1_0` — SeparatedBinInt (length 5) beats BinInt (`0b1`, length 3).
    [TestMethod]
    public void BinarySeparator_Succeeds()
        => AssertParsesV7("class C { int X = 0b1_0; }");

    // === POSITIVE (version 7): `throw` as expression. ===
    // The `throw` prefix operator (a reserved keyword, so no other Expression alternative starts with
    // it). The Cs1 `ThrowStatement` (`throw e;`) is a DIFFERENT rule and is unchanged.

    // Throw in the TRUE branch of a conditional: `c ? throw e : 5`. Roslyn PatternParsingTests.cs:77.
    [TestMethod]
    public void ThrowExpression_ConditionalTrueBranch_Succeeds()
        => AssertParsesV7("class C { int M(bool c, Exception e) { return c ? throw e : 5; } }");

    // Throw in the FALSE branch of a conditional: `c ? 5 : throw e`. Roslyn PatternParsingTests.cs:78.
    [TestMethod]
    public void ThrowExpression_ConditionalFalseBranch_Succeeds()
        => AssertParsesV7("class C { int M(bool c, Exception e) { return c ? 5 : throw e; } }");

    // Throw expression in an assignment (the task's form): `x = c ? throw e : null`.
    [TestMethod]
    public void ThrowExpression_Assignment_Succeeds()
        => AssertParsesV7("class C { Exception M(bool c, Exception e) { Exception x; x = c ? throw e : null; return x; } }");

    // Throw expression with a `new` operand (Roslyn's `throw new NullReferenceException()` form).
    [TestMethod]
    public void ThrowExpression_NewOperand_Succeeds()
        => AssertParsesV7("class C { int M(bool c) { return c ? throw new Exception() : 5; } }");

    // === POSITIVE (version 1, no-regression): no-separator literals + the throw STATEMENT. ===
    // A literal with NO `_` matches only the Cs1 terminal (the separated terminal requires a `_`), and
    // the `throw e;` statement parses via the UNCHANGED Cs1 ThrowStatement (the ThrowExpression is a
    // Cs7 feature, absent at v1). Both must stay green.

    [TestMethod]
    public void NoSeparatorInteger_ParsesAtV1()
        => AssertParsesV1("class C { int X = 1000000; }");

    [TestMethod]
    public void ThrowStatement_ParsesAtV1()
        => AssertParsesV1("class C { void M() { throw new Exception(); } }");

    // === NEGATIVE (version 6, version-purity): the CS7 forms are not available at v6. ===
    // At v6 the separated terminals are absent (so `1_000_000` = `1` + a dangling `_000_000`) and the
    // ThrowExpression is absent (so `throw e` in a conditional is not an expression).

    [TestMethod]
    public void IntegerSeparator_RejectedAtV6()
        => AssertFailsV6("class C { int X = 1_000_000; }");

    [TestMethod]
    public void ThrowExpression_RejectedAtV6()
        => AssertFailsV6("class C { int M(bool c, Exception e) { return c ? throw e : 5; } }");

    // === NEGATIVE (version 7, malformed). ===
    // Trailing separator: `100_` — no separated terminal matches (each `_` needs a following digit);
    // DecInt matches `100`, leaving a dangling `_` -> the parse fails. Roslyn Lexer.cs:838-841
    // (underscoreInWrongPlace -> ERR_InvalidNumber).

    [TestMethod]
    public void TrailingSeparator_Rejected()
        => AssertFailsV7("class C { int X = 100_; }");

    // === DOCUMENT (version 7, false premise, Deviation D1). ===
    // Leading separator: `_100` — the task expected a reject, but `_100` is a valid `Identifier`
    // (`[_\l]\w*`), so it parses as a VARIABLE reference, not a malformed numeric literal (Roslyn
    // likewise tokenizes a leading-`_` token as an identifier). The separated terminal requires a
    // leading digit, so it does not match `_100`; the identifier fallback wins -> the input PARSES.

    [TestMethod]
    public void LeadingSeparator_ParsesAsIdentifier()
        => AssertParsesV7("class C { int X = _100; }");

    // === POSITIVE (version 7): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void IntegerSeparator_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/LexicalAndXml/LexicalTests.cs:3057
        // (TestNumericWithUnderscores) — `1_000` = 1000. Adapted to a field initializer.
        AssertParsesV7("class C { int X = 1_000; }");
    }

    [TestMethod]
    public void RealSeparator_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/LexicalAndXml/LexicalTests.cs:3075
        // (TestNumericWithUnderscores) — `1_000.000_1` = 1000.0001 (separators in BOTH the integer and
        // the fractional part). Adapted to a field initializer.
        AssertParsesV7("class C { double X = 1_000.000_1; }");
    }

    [TestMethod]
    public void HexSeparator_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/LexicalAndXml/LexicalTests.cs:3102
        // (TestNumericWithUnderscores) — `0xA_A` = 0xAA. Adapted to a field initializer.
        AssertParsesV7("class C { int X = 0xA_A; }");
    }

    [TestMethod]
    public void BinarySeparator_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/LexicalAndXml/LexicalTests.cs:3111
        // (TestNumericWithUnderscores) — `0b1_1` = 3. Adapted to a field initializer.
        AssertParsesV7("class C { int X = 0b1_1; }");
    }

    [TestMethod]
    public void ThrowExpression_TrueBranch_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/PatternParsingTests.cs:77
        // (ThrowExpression_Good) — `int x = b ? throw new NullReferenceException() : 1;`. Adapted: a
        // local declaration whose initializer is a conditional with a throw in the TRUE branch.
        AssertParsesV7("class C { int M(bool b, Exception e) { int x = b ? throw e : 1; return x; } }");
    }

    [TestMethod]
    public void ThrowExpression_FalseBranch_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/PatternParsingTests.cs:78
        // (ThrowExpression_Good) — `x = b ? 2 : throw new NullReferenceException();`. Adapted: an
        // assignment whose RHS is a conditional with a throw in the FALSE branch.
        AssertParsesV7("class C { int M(bool b, Exception e) { int x; x = b ? 2 : throw e; return x; } }");
    }

    // === helpers ===

    private static void AssertParsesV7(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(7), input);

    private static void AssertFailsV7(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(7), input);

    private static void AssertParsesV6(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(6), input);

    private static void AssertFailsV6(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(6), input);

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
