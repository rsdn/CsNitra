using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.8.6 — C# 8.0 default interface members (methods with bodies in interfaces).
// Positives use CreateParser(8) (Cs1+...+Cs8 merged); version-purity negatives use CreateParser(7)
// (Cs1+...+Cs7, no CS8). A default interface method reuses MethodBody (Cs1.grammar:267 = `Block | ";"`,
// extended by Cs6.grammar:103-104 with the `=> Expression ";"` form) — the SAME body rule the class
// Method (Cs1.grammar:265) uses. The Cs8 re-declaration of InterfaceMethod (Cs8.grammar) APPENDS a
// body-bearing alternative to the Cs1 ";"-only form (T0.3 merge). Roslyn: a default interface method is
// a MethodDeclarationSyntax whose body is one of three mutually exclusive forms — `;` (no body), `{`
// (block), `=> expr ;` (expression) — parsed by ParseBlockAndExpressionBodiesWithSemicolon
// (LanguageParser.cs:3593-3631), called from ParseMethodDeclaration (LanguageParser.cs:3722). See
// docs/CSharpParserPlan-progressT3.8.6.md.
[TestClass]
public class Cs8DefaultInterfaceMembersTests
{
    // === POSITIVE (version 8): block body (empty). ===
    // `void M() { }` — the Cs1/Cs2 InterfaceMethod ";" alternatives fail (a `{` follows `)`, not `;`);
    // the Cs8 MethodBody=Block (Cs1.grammar:268) matches. Roslyn ParseBlockAndExpressionBodiesWithSemicolon
    // (LanguageParser.cs:3608-3610) -> ParseMethodOrAccessorBodyBlock.

    [TestMethod]
    public void BlockBody_Empty_Succeeds()
        => AssertParsesV8("interface I { void M() { } }");

    // === POSITIVE (version 8): block body with a statement. ===
    // `void M() { return; }` — MethodBody=Block = "{" Statement* "}" (Cs1.grammar:781); the `return;`
    // is a Cs1 ReturnStatement.

    [TestMethod]
    public void BlockBody_WithStatement_Succeeds()
        => AssertParsesV8("interface I { void M() { return; } }");

    // === POSITIVE (version 8): block body returning a value. ===
    // `int Get() { return 1; }` — the return type is `int`; the block body `return 1;` is a statement.

    [TestMethod]
    public void BlockBody_ReturnValue_Succeeds()
        => AssertParsesV8("interface I { int Get() { return 1; } }");

    // === POSITIVE (version 8): expression body (literal). ===
    // `void N() => 42;` — the ";" alternatives fail (a `=>` follows `)`, not `;`); the Cs8
    // MethodBody=ExpressionBody (`"=>" Expression ";"`, Cs6.grammar:104) matches. The `void`/`42`
    // return-type mismatch is a BINDER concern (parser-vs-binder split). Roslyn
    // ParseBlockAndExpressionBodiesWithSemicolon (LanguageParser.cs:3612-3621) ->
    // ParseArrowExpressionClause.

    [TestMethod]
    public void ExpressionBody_Literal_Succeeds()
        => AssertParsesV8("interface I { void N() => 42; }");

    // === POSITIVE (version 8): expression body with parameters and a binary expression. ===
    // `int Add(int a, int b) => a + b;` — the expression body's Expression is the Additive `a + b`;
    // the trailing `;` terminates the ExpressionBody.

    [TestMethod]
    public void ExpressionBody_WithParams_Succeeds()
        => AssertParsesV8("interface I { int Add(int a, int b) => a + b; }");

    // === POSITIVE (version 8): no body (semicolon) — still allowed at v8. ===
    // `void P();` — the Cs1 InterfaceMethod ";" alternative (loaded first) and the Cs8 MethodBody=";"
    // tie at the same length -> resolves to the Cs1 alternative -> the C# 1.0 interface method.

    [TestMethod]
    public void NoBody_Succeeds()
        => AssertParsesV8("interface I { void P(); }");

    // === POSITIVE (version 8): all three body forms in one interface (the task example). ===
    // `void M() { }` (block) + `void N() => 42;` (expression) + `void P();` (no body).

    [TestMethod]
    public void AllThreeBodyForms_Succeeds()
        => AssertParsesV8("interface I { void M() { } void N() => 42; void P(); }");

    // === POSITIVE (version 8): default interface method with the `new` modifier. ===
    // `new` is a MethodModifier (Cs1.grammar:297); the body is a block. Per-modifier legality is a
    // binder concern (parser-vs-binder split).

    [TestMethod]
    public void WithNewModifier_Succeeds()
        => AssertParsesV8("interface I { new void M() { } }");

    // === NEGATIVE (version 7, version-purity): a block body in an interface is CS8-only. ===
    // At v7 the Cs8 InterfaceMethod alternative is ABSENT, so InterfaceMethod is ";"-only; `void M() { }`
    // (a `{` after `)`) matches NO InterfaceMember alternative -> REJECTS. At v8 it PARSES.

    [TestMethod]
    public void BlockBody_RejectedAtV7()
        => AssertFailsV7("interface I { void M() { } }");

    // === NEGATIVE (version 7, version-purity): an expression body in an interface is CS8-only. ===
    // At v7 `void N() => 42;` (a `=>` after `)`) matches NO InterfaceMember alternative -> REJECTS.
    // At v8 it PARSES.

    [TestMethod]
    public void ExpressionBody_RejectedAtV7()
        => AssertFailsV7("interface I { void N() => 42; }");

    // === NO-REGRESSION (version 1): a no-body interface method parses at v1 (unchanged). ===
    // `void P();` is a C# 1.0 interface method (semicolon, no body); the Cs8 change does not touch it.

    [TestMethod]
    public void NoBody_ParsesAtV1()
        => AssertParsesV1("interface I { void P(); }");

    // === NO-REGRESSION (version 7): a no-body interface method parses at v7 (unchanged). ===

    [TestMethod]
    public void NoBody_ParsesAtV7()
        => AssertParsesV7("interface I { void P(); }");

    // === helpers ===

    private static void AssertParsesV8(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(8), input);

    private static void AssertFailsV7(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(7), input);

    private static void AssertParsesV1(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(1), input);

    private static void AssertParsesV7(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(7), input);

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
