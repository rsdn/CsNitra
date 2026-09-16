using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.6.2 — C# 7.0 pattern matching (Cs7.grammar). Positives use CreateParser(7) (Cs1+...+Cs7 merged);
// version-purity negatives use CreateParser(6) (Cs1+...+Cs6, no CS7). See
// docs/CSharpParserPlan-progressT3.6.2.md for the hand-traces. The Cs1 `is` is a TDOPP postfix
// (TypeIs, Relational level); the new Cs7 TypeIsPattern postfix extends further for the longer
// pattern forms, while TypeIs still wins the equal-length tie for a bare type (`x is int`), so the
// C# 1.0 type pattern is unchanged at every version.
[TestClass]
public class Cs7PatternTests
{
    // === POSITIVE (version 7): `is` type patterns. ===

    [TestMethod]
    public void TypePattern_IsInt_Succeeds()
        => AssertParsesV7("class C { void M() { if (x is int) { } } }");

    [TestMethod]
    public void TypePattern_IsString_Succeeds()
        => AssertParsesV7("class C { void M() { if (x is string) { } } }");

    // === POSITIVE (version 7): `is` declaration patterns. ===

    [TestMethod]
    public void DeclarationPattern_IsIntY_Succeeds()
        => AssertParsesV7("class C { void M() { if (x is int y) { N(y); } } }");

    // === POSITIVE (version 7): `is` constant patterns. ===

    [TestMethod]
    public void ConstantPattern_IsIntLiteral_Succeeds()
        => AssertParsesV7("class C { void M() { if (x is 5) { } } }");

    [TestMethod]
    public void ConstantPattern_IsStringLiteral_Succeeds()
        => AssertParsesV7("class C { void M() { if (x is \"a\") { } } }");

    // === POSITIVE (version 7): `is` discard. ===
    // `_` is a valid type name (QualifiedName), so Roslyn parses `x is _` as a TYPE pattern
    // (IdentifierName `_`), not a discard (PatternParsingTests.NotDiscardInIsTypeExpression, 5681).
    // It parses at every version; the CS7-specific forms are the declaration/switch/guard below.

    [TestMethod]
    public void Discard_IsUnderscore_Succeeds()
        => AssertParsesV7("class C { void M() { if (x is _) { } } }");

    // === POSITIVE (version 7): switch patterns. ===

    [TestMethod]
    public void SwitchDeclarationPattern_Succeeds()
        => AssertParsesV7("class C { void M() { switch (x) { case int y: N(y); break; } } }");

    [TestMethod]
    public void SwitchConstant_Succeeds()
        => AssertParsesV7("class C { void M() { switch (x) { case 5: N(); break; } } }");

    [TestMethod]
    public void SwitchGuard_Succeeds()
        => AssertParsesV7("class C { void M() { switch (x) { case int y when y > 0: N(y); break; } } }");

    [TestMethod]
    public void SwitchVarPattern_Succeeds()
        => AssertParsesV7("class C { void M() { switch (x) { case var y: N(y); break; } } }");

    // === POSITIVE (version 1 and 6): the type pattern is C# 1.0 — must stay green. ===

    [TestMethod]
    public void TypePattern_ParsesAtV1()
        => AssertParsesV1("class C { void M() { if (x is int) { } } }");

    [TestMethod]
    public void TypePattern_ParsesAtV6()
        => AssertParsesV6("class C { void M() { if (x is int) { } } }");

    // === POSITIVE (version 6): a constant switch case is C# 1.0 — must stay green. ===

    [TestMethod]
    public void SwitchConstant_ParsesAtV6()
        => AssertParsesV6("class C { void M() { switch (x) { case 5: N(); break; } } }");

    // === NEGATIVE (version 6, version-purity): the CS7 pattern constructs. ===

    [TestMethod]
    public void DeclarationPattern_RejectedAtV6()
        => AssertFailsV6("class C { void M() { if (x is int y) { } } }");

    [TestMethod]
    public void SwitchPattern_RejectedAtV6()
        => AssertFailsV6("class C { void M() { switch (x) { case int y: break; } } }");

    [TestMethod]
    public void SwitchGuard_RejectedAtV6()
        => AssertFailsV6("class C { void M() { switch (x) { case int y when y > 0: break; } } }");

    [TestMethod]
    public void SwitchVarPattern_RejectedAtV6()
        => AssertFailsV6("class C { void M() { switch (x) { case var y: break; } } }");

    // === NEGATIVE (version 7, malformed). ===

    [TestMethod]
    public void IsMissingPattern_Rejected()
        => AssertFailsV7("class C { void M() { if (x is) { } } }");

    // `case int when y > 0:` — `when` is a true identifier (not reserved), so the DeclarationPattern
    // consumes it as the designation (`int when`), leaving `y > 0:` unmatched before the `:`. REJECTS.
    // (Roslyn would ACCEPT this as a type pattern + guard, because it excludes `when` from
    // designations in a switch arm — a documented boundary, see the progress doc.)
    [TestMethod]
    public void SwitchGuardMissingDeclaration_Rejected()
        => AssertFailsV7("class C { void M() { switch (x) { case int when y > 0: break; } } }");

    // === POSITIVE (version 7): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void IsDeclarationPattern_Conditional_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationExpressionTests.cs:227
        // (TestIfDeclarationExpression) — `if (e is int x ? true : false) {}`. The `is` (Relational)
        // binds tighter than `?:` (Conditional), so it is `(e is int x) ? true : false`. Adapted to a
        // full method body.
        AssertParsesV7("class C { void M() { if (e is int x ? true : false) { } } }");
    }

    [TestMethod]
    public void IsDeclarationPattern_DiscardDesignation_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeconstructionTests.cs:2676 —
        // `if (e is int _) {}` is a declaration pattern whose designation is the discard `_`.
        // Adapted to a full method body.
        AssertParsesV7("class C { void M() { if (e is int _) { } } }");
    }

    [TestMethod]
    public void SwitchVarPattern_DiscardDesignation_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeconstructionTests.cs:2865 —
        // `switch (e) { case var _: break; }` is a var pattern whose designation is the discard `_`.
        // Adapted to a full method body.
        AssertParsesV7("class C { void M() { switch (e) { case var _: break; } } }");
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
