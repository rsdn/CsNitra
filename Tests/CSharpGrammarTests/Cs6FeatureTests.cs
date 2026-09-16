using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.5.3 — C# 6.0: `?.`, expression-bodied members, `nameof`, binary literals (Cs6.grammar).
// Positives use CreateParser(6) (Cs1+Cs2+Cs3+Cs4+Cs5+Cs6 merged); version-purity negatives use
// CreateParser(5) (Cs1+Cs2+Cs3+Cs4+Cs5, no CS6). See docs/CSharpParserPlan-progressT3.5.3.md for the
// hand-traces and the documented deviations (the contextual-keyword `nameof`/binary-literal edge cases).
[TestClass]
public class Cs6FeatureTests
{
    // === POSITIVE (version 6): the `?.` null-conditional operator. ===

    [TestMethod]
    public void NullConditional_Invocation_Succeeds()
        => AssertParsesV6("class C { void M() { x?.M(); } }");

    [TestMethod]
    public void NullConditional_MemberAccess_Succeeds()
        => AssertParsesV6("class C { void M() { var y = x?.P; } }");

    [TestMethod]
    public void NullConditional_Indexer_Succeeds()
        => AssertParsesV6("class C { void M() { x?.[i]; } }");

    // Chained: `?.M()` (null-conditional member access + invocation) followed by a plain `.P`
    // member access. Roslyn: `x?.y` is a postfix primary operator (LanguageParser.cs:11945).
    [TestMethod]
    public void NullConditional_ChainedMemberAccess_Succeeds()
        => AssertParsesV6("class C { void M() { var y = x?.M().P; } }");

    // === POSITIVE (version 6): expression-bodied members. ===

    [TestMethod]
    public void ExpressionBodied_Method_Succeeds()
        => AssertParsesV6("class C { int M() => 5; }");

    [TestMethod]
    public void ExpressionBodied_Property_Succeeds()
        => AssertParsesV6("class C { int P => _x; }");

    // A statement-expression body (an expression that is a method call). Roslyn ParseArrowExpressionClause.
    [TestMethod]
    public void ExpressionBodied_StatementExpression_Succeeds()
        => AssertParsesV6("class C { void M() => N(); }");

    // A complex expression body (a binary operator).
    [TestMethod]
    public void ExpressionBodied_Method_ComplexExpression_Succeeds()
        => AssertParsesV6("class C { int M() => a + b; }");

    // === POSITIVE (version 6): `nameof`. ===

    [TestMethod]
    public void NameOf_Identifier_Succeeds()
        => AssertParsesV6("class C { void M() { var s = nameof(x); } }");

    // A member access (dotted) argument.
    [TestMethod]
    public void NameOf_MemberAccess_Succeeds()
        => AssertParsesV6("class C { void M() { var s = nameof(C.P); } }");

    // A nested (multi-segment) member access argument.
    [TestMethod]
    public void NameOf_NestedMemberAccess_Succeeds()
        => AssertParsesV6("class C { void M() { var s = nameof(C.P.Q); } }");

    // === POSITIVE (version 6): binary literals. ===

    [TestMethod]
    public void BinaryLiteral_LowerPrefix_Succeeds()
        => AssertParsesV6("class C { void M() { int x = 0b1010; } }");

    [TestMethod]
    public void BinaryLiteral_UpperPrefix_Succeeds()
        => AssertParsesV6("class C { void M() { int x = 0B1010; } }");

    // A binary literal with an integer suffix.
    [TestMethod]
    public void BinaryLiteral_WithSuffix_Succeeds()
        => AssertParsesV6("class C { void M() { long x = 0b1010L; } }");

    // === POSITIVE (version 1-5): `nameof` as a plain IDENTIFIER (must stay green). ===
    // `nameof` is a contextual keyword (NOT reserved), so it remains a valid name at every version.
    // A field NAMED `nameof` parses at v1 and v5.
    [TestMethod]
    public void NameOf_AsFieldName_ParsesAtV1()
        => AssertParsesV1("class C { int nameof; }");

    [TestMethod]
    public void NameOf_AsFieldName_ParsesAtV5()
        => AssertParsesV5("class C { int nameof; }");

    // === NEGATIVE (version 5, version-purity): all four features are CS6-only. ===

    [TestMethod]
    public void NullConditional_RejectedAtV5()
        => AssertFailsV5("class C { void M() { x?.M(); } }");

    [TestMethod]
    public void ExpressionBodied_Method_RejectedAtV5()
        => AssertFailsV5("class C { int M() => 5; }");

    [TestMethod]
    public void BinaryLiteral_RejectedAtV5()
        => AssertFailsV5("class C { void M() { int x = 0b1010; } }");

    // === NEGATIVE (version 6, malformed). ===

    // Missing expression body: `=> ;` (no expression after the arrow).
    [TestMethod]
    public void ExpressionBodied_MissingBody_Rejected()
        => AssertFailsV6("class C { int M() => ; }");

    // Invalid binary digit: `0b102` (the `2` is not a binary digit). BinInt matches `0b10`, leaving a
    // dangling `2` -> the whole parse fails.
    [TestMethod]
    public void BinaryLiteral_InvalidDigit_Rejected()
        => AssertFailsV6("class C { void M() { int x = 0b102; } }");

    // === DOCUMENT (version 6): contextual-keyword edge cases (actual behavior, deviations). ===

    // `x?.M` (a null-conditional member access WITHOUT an invocation) is a COMPLETE, valid C# 6.0
    // expression (it evaluates to the member value, or null if `x` is null). As a statement expression
    // it PARSES — this is NOT malformed. Documented as a positive (the task listed it as "incomplete —
    // verify; document"; the verification shows it is a valid null-conditional member access).
    [TestMethod]
    public void NullConditional_MemberAccessOnly_ParsesAtV6()
        => AssertParsesV6("class C { void M() { x?.M; } }");

    // `nameof()` (an empty argument list) PARSES at v6 as a method INVOCATION on a variable named
    // `nameof` (a valid IdentifierName, since `nameof` is a contextual keyword and NOT reserved). The
    // NameOf alternative fails (no argument), so the IdentifierName+Invocation path wins. This is a
    // documented deviation from Roslyn (where `nameof()` is a parse error because the argument is
    // required) — the same contextual-keyword behavior as `await;` in T3.4. Covered as a positive
    // (documents the actual behavior), not a reject.
    [TestMethod]
    public void NameOf_EmptyArgument_ParsesAsInvocationAtV6()
        => AssertParsesV6("class C { void M() { var s = nameof(); } }");

    // VERSION-PURITY DEVIATION (documented): `nameof(x)` PARSES at v5 (NOT a reject) as a method
    // INVOCATION on a variable named `nameof`. The task listed `nameof(x)` as a v5 version-purity
    // negative, but `nameof` is a CONTEXTUAL keyword (a valid IdentifierName at every version, NOT
    // reserved — per the task's own instruction "Do NOT add it to ReservedKeyword"), so `nameof(x)` is
    // a syntactically valid method invocation at v5 (the NameOf alternative is absent at v5, so the
    // IdentifierName+Invocation path wins). This matches real C#: `nameof(x)` is syntactically valid
    // at C# 5.0 (a method call); the nameof FEATURE (returning the name as a string) is a BINDER-level
    // version gate, not a parse-level one. The same contextual-keyword behavior as `await;` in T3.4.
    // Covered as a positive (documents the actual behavior). The clean v5-vs-v6 discriminator for the
    // nameof EXPRESSION is that at v6 `nameof(x)` is a NameOf primary (longer than IdentifierName),
    // while at v5 it is a method invocation.
    [TestMethod]
    public void NameOf_ParsesAsInvocationAtV5()
        => AssertParsesV5("class C { void M() { var s = nameof(x); } }");

    // === helpers ===

    private static void AssertParsesV6(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(6), input);

    private static void AssertFailsV6(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(6), input);

    private static void AssertParsesV5(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(5), input);

    private static void AssertFailsV5(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(5), input);

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
