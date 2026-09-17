using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.7.1 — C# 7.1 `default` literal (Cs7.grammar) + the C# 1.0 typed default `default ( Type )`
// (Cs1.grammar, added in this task — see the progress doc for why).
//
// Two `default` EXPRESSION forms (Roslyn ParseDefaultExpression, LanguageParser.cs:12633):
//   * the `default` LITERAL (C# 7.1) — the `default` keyword ALONE, no `(...)`, no `:`.
//     `int x = default;`. A new Primary alternative in Cs7 (DefaultLiteral). Version gate:
//     IDS_FeatureDefaultLiteral (MessageID.cs:142), a SEMANTIC check (Binder_Expressions.cs:753).
//   * the typed default `default ( Type )` (C# 1.0) — a new Primary alternative in Cs1 (DefaultTyped).
//
// The `default:` switch label is the UNCHANGED Cs1 DefaultLabel (Cs1.grammar:904) — a SwitchLabel,
// not an expression. Positives use CreateParser(7) (Cs1+...+Cs7 merged); the version-purity negative
// uses CreateParser(6) (Cs1+...+Cs6, no CS7). See docs/CSharpParserPlan-progressT3.7.1.md for the
// hand-traces.
[TestClass]
public class Cs7DefaultLiteralTests
{
    // === POSITIVE (version 7): default literal in a local-variable initializer. ===
    // The `default` is a bare Primary (DefaultLiteral, Cs7). Roslyn ParseDefaultExpression (12633)
    // else-branch → DefaultLiteralExpression.

    [TestMethod]
    public void DefaultLiteral_LocalVariable_Succeeds()
        => AssertParsesV7("class C { int M() { int x = default; return x; } }");

    // === POSITIVE (version 7): default literal in a return. ===

    [TestMethod]
    public void DefaultLiteral_Return_Succeeds()
        => AssertParsesV7("class C { int M() { return default; } }");

    // === POSITIVE (version 7): default literal as a method argument. ===
    // The argument is a PositionalArgument (Cs4 Argument) whose Expression is the DefaultLiteral.

    [TestMethod]
    public void DefaultLiteral_Argument_Succeeds()
        => AssertParsesV7("class C { void M() { N(default); } void N(int x) { } }");

    // === POSITIVE (version 7): default literal in an assignment. ===

    [TestMethod]
    public void DefaultLiteral_Assignment_Succeeds()
        => AssertParsesV7("class C { int M() { int x; x = default; return x; } }");

    // === POSITIVE (version 6): no-regression — the C# 1.0 typed default `default ( Type )`. ===
    // The task listed this as a "version 1–6, must stay green" test. It did NOT parse at any version
    // before T3.7.1 (the `default ( Type )` form was missing — see the progress doc, Deviation D1).
    // T3.7.1 adds it to Cs1 (DefaultTyped), so it now parses at v1–v6 (and v7). At v6 it is the sole
    // match for `default ( ... )` (the Cs7 literal is absent). Roslyn ParseDefaultExpression (12633)
    // typed branch: `default` + `(` + ParseType() + `)`.

    [TestMethod]
    public void DefaultTyped_ParsesAtV6()
        => AssertParsesV6("class C { int M() { return default(int); } }");

    // === POSITIVE (version 6): no-regression — the C# 1.0 `default:` switch label. ===
    // The `default:` is a DefaultLabel (Cs1.grammar:904), a SwitchLabel, NOT an expression. It is
    // untouched by the new Primary alternatives (which are only reached in expression contexts).

    [TestMethod]
    public void DefaultLabel_ParsesAtV6()
        => AssertParsesV6("class C { void M() { switch (x) { default: break; } } }");

    // === NEGATIVE (version 6, version-purity): the `default` literal is not available at v6. ===
    // At v6 the DefaultLiteral (Cs7) is absent, and `default` alone matches no other Primary
    // alternative (`default` is a reserved keyword, not an IdentifierName) → REJECTS. At v7 it PARSES.

    [TestMethod]
    public void DefaultLiteral_RejectedAtV6()
        => AssertFailsV6("class C { int M() { int x = default; return x; } }");

    // === NEGATIVE (version 7, malformed): a typed default missing its type / unclosed paren. ===
    // `default( ;` — DefaultTyped fails (no Type after `(`); the literal branch gives `default`
    // (bare), then `(` is found where `;` is expected → the LocalVariableDeclaration fails → REJECTS.
    // NOTE: the task's suggested "trailing space" case (`int x = default ;`) is NOT malformed — trivia
    // is skipped after every terminal, so it PARSES (see DefaultLiteral_TrailingSpace_Parses).

    [TestMethod]
    public void DefaultLiteral_Malformed_UnclosedParen_Rejected()
        => AssertFailsV7("class C { int M() { int x = default( ; return x; } }");

    // === DOCUMENT (version 7): the task's "trailing space" case PARSES (trivia is skipped). ===
    // `int x = default ;` — the space between `default` and `;` is trivia (skipped after every
    // terminal), so the initializer is the bare `default` literal and the declaration is valid. This
    // documents that the "trailing space" is NOT a malformed case (the task asked to verify).

    [TestMethod]
    public void DefaultLiteral_TrailingSpace_Parses()
        => AssertParsesV7("class C { int M() { int x = default ; return x; } }");

    // === POSITIVE (version 7): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void DefaultTyped_Roslyn_Generic_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/AwaitParsingTests.cs:638
        // (`async () => await default(Task);`) — a typed default `default ( Type )`. Adapted to a
        // generic method returning `default(T)` (a generic type parameter as the Type).
        AssertParsesV7("class C { T M<T>() { return default(T); } }");
    }

    [TestMethod]
    public void DefaultTyped_Roslyn_AsArgument_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ForStatementParsingTest.cs:2885
        // (`for (default(int);default(int);default(int));`) — a typed default `default(int)`. Adapted
        // to a method argument `N(default(int))`.
        AssertParsesV7("class C { void M() { N(default(int)); } void N(int x) { } }");
    }

    [TestMethod]
    public void DefaultLiteral_Roslyn_Multiple_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:4769
        // (TestTargetTypedDefaultWithCSharp7_1) — `default` parses as a DefaultLiteralExpression at
        // C# 7.1. Adapted to two default literals in one argument list.
        AssertParsesV7("class C { void M() { N(default, default); } void N(object a, object b) { } }");
    }

    [TestMethod]
    public void DefaultTyped_Roslyn_Qualified_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ForStatementParsingTest.cs:2885 (adapted) —
        // a typed default with a QUALIFIED type name `default(System.String)` (Type = QualifiedName).
        AssertParsesV7("class C { void M() { N(default(System.String)); } void N(object x) { } }");
    }

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
