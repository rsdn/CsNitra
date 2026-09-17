using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.9.2 — C# 9.0 `with` expression (record with expression, Cs9.grammar). Positives use
// CreateParser(9) (Cs1+...+Cs9 merged); version-purity negatives use CreateParser(8) (Cs1+...+Cs8, no
// CS9). The `with` expression is a POSTFIX expression: a record (primary) expression followed by
// `with { ... }`. Modeled as a PostfixOp alternative (WithExpr = "with" Initializer), applied inside
// PrimaryExpr = Primary PostfixOp* (Cs1.grammar:637). The `{ ... }` body reuses the Cs3 Initializer
// (Cs3.grammar:103); its InitializerElement handles both a member assignment (`X = 10`) and a plain
// expression. See docs/CSharpParserPlan-progressT3.9.2.md.
//
// Roslyn: ParseWithExpression (LanguageParser.cs:13458-13479); the `with` operator is recognized only
// when a `{` follows the `with` keyword (GetOperatorExpressionKind, 11785-11786).
[TestClass]
public class Cs9WithExpressionTests
{
    // === POSITIVE (version 9): a variable record expression `with { ... }`. ===
    // Roslyn ParseWithExpression (LanguageParser.cs:13458): receiver `p`, `with { X = 10 }`.

    [TestMethod]
    public void WithExpression_Variable_Succeeds()
        => AssertParsesV9("class C { void M() { var p2 = p with { X = 10 }; } }");

    // === POSITIVE (version 9): a `new` expression `with { ... }`. ===
    // The receiver is a NewExpr Primary (`new Point(1, 2)`); `with { Y = 20 }` is a PostfixOp.

    [TestMethod]
    public void WithExpression_NewExpression_Succeeds()
        => AssertParsesV9("class C { void M() { var p3 = new Point(1, 2) with { Y = 20 }; } }");

    // === POSITIVE (version 9): multiple property assignments in the `with` initializer. ===

    [TestMethod]
    public void WithExpression_MultipleProperties_Succeeds()
        => AssertParsesV9("class C { void M() { var p = p with { X = 10, Y = 20 }; } }");

    // === POSITIVE (version 9): empty `with` initializer (`p with { }` copies with no changes). ===
    // Roslyn requireOneElement: false (LanguageParser.cs:13468); the reused Initializer allows zero
    // elements.

    [TestMethod]
    public void WithExpression_EmptyInitializer_Succeeds()
        => AssertParsesV9("class C { void M() { var p = p with { }; } }");

    // === POSITIVE (version 9): `with` after a member-access record expression. ===

    [TestMethod]
    public void WithExpression_MemberAccessReceiver_Succeeds()
        => AssertParsesV9("class C { void M() { var p = o.Point with { X = 1 }; } }");

    // === VERSION-PURITY (version 8, no CS9): the `with` expression must REJECT at v8. ===
    // At v8 the WithExpr PostfixOp alternative is ABSENT (Cs9-only); `p with { X = 10 }` leaves the
    // `with { }` unconsumed -> REJECTS. At v9 it PARSES.

    [TestMethod]
    public void WithExpression_RejectedAtV8()
        => AssertFailsV8("class C { void M() { var p2 = p with { X = 10 }; } }");

    // === VERSION-PURITY (version 8, no CS9): a `new` expression `with { ... }` at v8. ===

    [TestMethod]
    public void WithExpression_NewExpression_RejectedAtV8()
        => AssertFailsV8("class C { void M() { var p3 = new Point(1, 2) with { Y = 20 }; } }");

    // === NEGATIVE (version 9, malformed): `with` without a `{ ... }` initializer is invalid. ===
    // Roslyn GetOperatorExpressionKind (11785-11786): the `with` operator is recognized only when a
    // `{` follows; a bare `with` leaves the expression unconsumed -> REJECTS.

    [TestMethod]
    public void WithExpression_MissingInitializer_Rejected()
        => AssertFailsV9("class C { void M() { var p = p with; } }");

    // === NEGATIVE (version 9, malformed): trailing comma in the `with` initializer. ===
    // The reused Cs3 Initializer uses the default (Forbidden) trailing-separator behavior, so
    // `p with { X = 10, }` REJECTS (a minor deviation from Roslyn's allowTrailingSeparator: true).

    [TestMethod]
    public void WithExpression_TrailingComma_Rejected()
        => AssertFailsV9("class C { void M() { var p = p with { X = 10, }; } }");

    // === helpers ===

    private static void AssertParsesV9(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(9), input);

    private static void AssertFailsV9(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(9), input);

    private static void AssertFailsV8(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(8), input);

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
