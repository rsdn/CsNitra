using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.8.1 — C# 8.0 switch expressions (Cs8.grammar). Positives use CreateParser(8) (Cs1+...+Cs8
// merged); version-purity negatives use CreateParser(7) (Cs1+...+Cs7, no CS8). See
// docs/CSharpParserPlan-progressT3.8.1.md for the hand-traces. The switch expression is a TDOPP
// POSTFIX on Expression at the Unary level: `Expression : Unary "switch" "{" SwitchArm (","
// SwitchArm)* "}"`, each arm `Pattern "=>" Expression : Comma` (the T3.6.2 Pattern + the `=>` arrow
// + an expression that stops at the `,` arm separator). The `switch` keyword is reserved, so the
// postfix is mutually exclusive with every existing postfix. The switch STATEMENT (Cs1) is a
// different rule and is untouched (no regression at v1-v7).
[TestClass]
public class Cs8SwitchExpressionTests
{
    // === POSITIVE (version 8): basic switch expression. ===
    // `x switch { 0 => "zero", _ => "other" }` — the SwitchExpression postfix applies to the prefix
    // `x`; arms are a constant pattern (`0`) and a discard (`_`). Roslyn ParseSwitchExpression
    // (LanguageParser_Patterns.cs:593).

    [TestMethod]
    public void SwitchExpression_Basic_Succeeds()
        => AssertParsesV8("class C { string M(int x) { return x switch { 0 => \"zero\", _ => \"other\" }; } }");

    // === POSITIVE (version 8): multiple arms. ===

    [TestMethod]
    public void SwitchExpression_MultipleArms_Succeeds()
        => AssertParsesV8("class C { string M(int x) { return x switch { 0 => \"zero\", 1 => \"one\", 2 => \"two\", _ => \"other\" }; } }");

    // === POSITIVE (version 8): type patterns in arms. ===
    // `int i` / `string s` are DeclarationPatterns (Type + Identifier); `_` is a DiscardPattern.

    [TestMethod]
    public void SwitchExpression_TypePatterns_Succeeds()
        => AssertParsesV8("class C { string M(object x) { return x switch { int i => \"int\", string s => \"string\", _ => \"other\" }; } }");

    // === POSITIVE (version 8): declaration patterns whose value is the designation. ===

    [TestMethod]
    public void SwitchExpression_DeclarationPatterns_Succeeds()
        => AssertParsesV8("class C { int M(object x) { return x switch { int i => i, _ => 0 }; } }");

    // === POSITIVE (version 8): switch expression in a return. ===

    [TestMethod]
    public void SwitchExpression_InReturn_Succeeds()
        => AssertParsesV8("class C { int M(int x) { return x switch { 0 => 100, _ => x }; } }");

    // === POSITIVE (version 1 and 7, no-regression): the switch STATEMENT. ===
    // The switch STATEMENT (Cs1 SwitchStatement, "switch" "(" Expression ")" "{" SwitchSection* "}")
    // is a DIFFERENT rule from the switch EXPRESSION (an Expression postfix). It is untouched by Cs8,
    // so it still parses at every version (the task requires v1-v7).

    [TestMethod]
    public void SwitchStatement_ParsesAtV1()
        => AssertParsesV1("class C { void M(int x) { switch (x) { case 0: break; default: break; } } }");

    [TestMethod]
    public void SwitchStatement_ParsesAtV7()
        => AssertParsesV7("class C { void M(int x) { switch (x) { case 0: break; default: break; } } }");

    // === NEGATIVE (version 7, version-purity): the switch expression is not available at v7. ===
    // At v7 the SwitchExpression postfix is absent (Cs8-only); after the prefix `x`, no postfix
    // matches the reserved `switch` -> the expression is just `x`, leaving `switch {...}` unconsumed
    // -> REJECTS. At v8 it PARSES.

    [TestMethod]
    public void SwitchExpression_RejectedAtV7()
        => AssertFailsV7("class C { string M(int x) { return x switch { 0 => \"zero\", _ => \"other\" }; } }");

    // === NEGATIVE (version 8, malformed): a missing arm expression. ===
    // `0 =>,` — the arm is `Pattern "=>" Expression : Comma`; after `=>` the next token is `,` (not an
    // expression start), so the arm's Expression fails -> the first SwitchArm fails -> the switch
    // expression (which requires >= 1 arm) fails -> the postfix fails -> `switch {...}` is left
    // unconsumed -> REJECTS. (Roslyn would recover this with an error; this grammar rejects it.)

    [TestMethod]
    public void SwitchExpression_MissingArmExpression_Rejected()
        => AssertFailsV8("class C { int M(int x) { return x switch { 0 =>, _ => 0 }; } }");

    // === POSITIVE (version 8): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void SwitchExpression_Discard_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/PatternParsingTests.cs:5457
        // (DiscardInSwitchExpression) — `e switch { _ => 1 }` (a discard arm). Adapted to a method
        // returning the switch expression.
        AssertParsesV8("class C { int M(object e) { return e switch { _ => 1 }; } }");
    }

    [TestMethod]
    public void SwitchExpression_Chained_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/PatternParsingTests.cs:7443
        // (ChainedSwitchExpression_01) — `1 switch { 1 => 2 } switch { 2 => 3 }` (a switch expression
        // whose governing expression is ITSELF a switch expression). The inner switch is a postfix on
        // the prefix `1`; the outer SwitchExpression postfix applies to that result. Adapted to a
        // method returning the chained form.
        AssertParsesV8("class C { int M() { return 1 switch { 1 => 2 } switch { 2 => 3 }; } }");
    }

    [TestMethod]
    public void SwitchExpression_ConstantPattern_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/PatternParsingTests.cs:2476
        // (SwitchExpression01) — `1 switch {a => b, c => d}`. In Roslyn `a`/`c` are CONSTANT patterns
        // (identifier names); in this grammar a bare identifier pattern resolves to the TypePattern
        // (the first equal-length Pattern alternative) rather than the ConstantPattern — a documented
        // semantic difference (syntax still parses). Adapted to a method returning the switch
        // expression (the identifier values `b`/`d` are expressions).
        AssertParsesV8("class C { object M() { return 1 switch { a => b, c => d }; } }");
    }

    [TestMethod]
    public void SwitchExpression_MethodCallArmValue_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/PatternParsingTests.cs:2476 (SwitchExpression01,
        // adapted) — an arm value that is a method CALL (a PrimaryExpr with an Invocation postfix).
        // `Expression : Comma` (minPrecedence 1) still applies the Invocation postfix (bp > 1), so the
        // call is the arm value and it stops at the `,` arm separator.
        AssertParsesV8("class C { int M(int x) { return x switch { 0 => N(), _ => 0 }; } int N() { return 0; } }");
    }

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
