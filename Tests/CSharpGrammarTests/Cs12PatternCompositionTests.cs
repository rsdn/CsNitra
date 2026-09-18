using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.12.4 — C# 12.0 `not`/`and`/`or` pattern composition (Cs12.grammar). A pattern can be composed
// with the contextual keywords `not` (prefix, highest precedence), `and` (binary, middle), `or`
// (binary, lowest): `if (x is not null)`, `if (x is > 0 and < 10)`, `if (x is 1 or 2 or 3)`. The
// relational pattern (`> 0`, `< 10`) is a PRIMARY pattern required for the `and` test. Positives use
// CreateParser(12) (Cs1+...+Cs12 merged); version-purity negatives use CreateParser(11) (no CS12).
// See docs/CSharpParserPlan-progressT3.12.4.md.
//
// Each input is wrapped in a version-neutral method-body context (`class C { void M() { ... } }`) so
// the ONLY CS12 feature is the pattern composition / relational pattern: at v11 the
// DisjunctivePattern / RelationalPattern alternatives (and the PrimaryPattern / ConjunctivePattern /
// NegatedPattern rules) are ABSENT, so a composed / relational pattern matches no Pattern alternative
// and REJECTS, while at v12 they are present and ACCEPT.
[TestClass]
public class Cs12PatternCompositionTests
{
    // === POSITIVE (version 12): `not` pattern (highest precedence). ===
    // Roslyn ParseNegatedPattern (LanguageParser_Patterns.cs:158-185): `not` + a pattern. `null` is a
    // ConstantPattern (an Expression).

    [TestMethod]
    public void NotPattern_NotNull_Succeeds()
        => AssertParsesV12("class C { void M() { if (x is not null) { } } }");

    // === POSITIVE (version 12): `not` over a type pattern. ===

    [TestMethod]
    public void NotPattern_Type_Succeeds()
        => AssertParsesV12("class C { void M() { if (x is not int) { } } }");

    // === POSITIVE (version 12): `and` pattern (middle precedence) over two relational patterns. ===
    // Roslyn ParseConjunctivePattern (104-117) + ParsePrimaryPattern relational (217-228): `> 0` and
    // `< 10` are relational patterns, joined by `and`.

    [TestMethod]
    public void AndPattern_Relational_Succeeds()
        => AssertParsesV12("class C { void M() { if (x is > 0 and < 10) { } } }");

    // === POSITIVE (version 12): `and` over two type patterns. ===

    [TestMethod]
    public void AndPattern_Type_Succeeds()
        => AssertParsesV12("class C { void M() { if (x is int and string) { } } }");

    // === POSITIVE (version 12): `or` pattern (lowest precedence) over constant patterns. ===
    // Roslyn ParseDisjunctivePattern (58-71): `1 or 2 or 3` is a loop of `or`-separated patterns.

    [TestMethod]
    public void OrPattern_Constant_Succeeds()
        => AssertParsesV12("class C { void M() { if (x is 1 or 2 or 3) { } } }");

    // === POSITIVE (version 12): a simple relational pattern (no composition). ===
    // A relational pattern is a PRIMARY pattern; it must parse on its own (not only inside a
    // composition). Roslyn ParsePrimaryPattern (217-228).

    [TestMethod]
    public void RelationalPattern_Simple_Succeeds()
        => AssertParsesV12("class C { void M() { if (x is > 0) { } } }");

    // === POSITIVE (version 12, regression): a C# 7.0 type `is` pattern still parses at v12. ===
    // The composition / relational alternatives must not disturb the existing type pattern (TypeIs,
    // Cs1.grammar:706 / TypeIsPattern, Cs7.grammar:108). For a bare type the FIRST base alternative
    // (TypePattern) wins the equal-length tie with the DisjunctivePattern path.

    [TestMethod]
    public void TypePattern_IsInt_StillSucceedsAtV12()
        => AssertParsesV12("class C { void M() { if (x is int) { } } }");

    // === NEGATIVE (version 11, version-purity): `not` pattern is not available at v11. ===
    // At v11 the DisjunctivePattern / NegatedPattern are ABSENT; `not` matches only as a type/constant
    // (the FIRST base alternative), leaving `null` unconsumed -> the `if` fails -> REJECTS.

    [TestMethod]
    public void NotPattern_NotNull_RejectedAtV11()
        => AssertFailsV11("class C { void M() { if (x is not null) { } } }");

    // === NEGATIVE (version 11, version-purity): `and` over relational patterns at v11. ===
    // At v11 the RelationalPattern / DisjunctivePattern are ABSENT, so `> 0` matches no Pattern
    // alternative -> the `is` fails -> REJECTS.

    [TestMethod]
    public void AndPattern_Relational_RejectedAtV11()
        => AssertFailsV11("class C { void M() { if (x is > 0 and < 10) { } } }");

    // === NEGATIVE (version 11, version-purity): `or` over constants at v11. ===
    // At v11 the DisjunctivePattern is ABSENT; the ConstantPattern matches only `1`, leaving
    // `or 2 or 3` unconsumed -> the `if` fails -> REJECTS.

    [TestMethod]
    public void OrPattern_Constant_RejectedAtV11()
        => AssertFailsV11("class C { void M() { if (x is 1 or 2 or 3) { } } }");

    // === NEGATIVE (version 11, version-purity): a simple relational pattern at v11. ===
    // At v11 the RelationalPattern is ABSENT, so `> 0` matches no Pattern alternative -> REJECTS.

    [TestMethod]
    public void RelationalPattern_Simple_RejectedAtV11()
        => AssertFailsV11("class C { void M() { if (x is > 0) { } } }");

    // === NEGATIVE (version 12, malformed): a trailing `or` with no right-hand pattern. ===
    // The DisjunctivePattern requires a ConjunctivePattern after each `or`; the `)` is not a pattern,
    // so the composition fails and the `if` cannot close -> REJECTS.

    [TestMethod]
    public void OrPattern_TrailingOperator_Rejected()
        => AssertFailsV12("class C { void M() { if (x is 1 or) { } } }");

    // === helpers ===

    private static void AssertParsesV12(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(12), input);

    private static void AssertFailsV12(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(12), input);

    private static void AssertFailsV11(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(11), input);

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
