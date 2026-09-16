using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.4 — C# 5.0 `async`/`await` (Cs5.grammar). Positives use CreateParser(5) (Cs1+Cs2+Cs3+Cs4+Cs5
// merged); version-purity negatives use CreateParser(4) (Cs1+Cs2+Cs3+Cs4, no CS5).
//
// `async` is a METHOD MODIFIER (appended to the Cs1 MethodModifier union) and `await` is a UNARY
// PREFIX expression operator (appended to the Cs1 Expression TDOPP rule as `AwaitExpr = "await"
// Expression : Unary`, the same level as the Cs1 `-`/`!`/`~` prefix operators). Both are CONTEXTUAL
// keywords: plain identifiers in C# 1.0–4.0, keywords in C# 5.0+. They are NOT reserved (so `async`/
// `await` remain valid names at every version); the `async` MODIFIER and the `await` EXPRESSION are
// reachable only at v5+. See docs/CSharpParserPlan-progressT3.4.md for the hand-traces.
[TestClass]
public class Cs5AsyncAwaitTests
{
    // === POSITIVE (version 5): the `async` method modifier. ===

    [TestMethod]
    public void AsyncVoidMethod_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/AsyncParsingTests.cs:38 (SimpleAsyncMethod).
        AssertParsesV5("class C { async void M() { } }");
    }

    [TestMethod]
    public void AsyncTaskMethod_Await_Succeeds()
        => AssertParsesV5("class C { async Task M() { await N(); } }");

    [TestMethod]
    public void AsyncVoidMethod_VarAwait_Succeeds()
        => AssertParsesV5("class C { async void M() { var x = await N(); } }");

    [TestMethod]
    public void AsyncTaskMethod_AwaitTaskDelay_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:3214 —
        // `await Task.Delay()` (an AwaitExpression whose operand is an InvocationExpression on the
        // member-access Task.Delay). Adapted with an argument.
        AssertParsesV5("class C { async Task M() { await Task.Delay(100); } }");
    }

    [TestMethod]
    public void PublicAsyncVoidMethod_Succeeds()
        => AssertParsesV5("class C { public async void M() { } }");

    [TestMethod]
    public void StaticAsyncTaskMethod_Succeeds()
        => AssertParsesV5("class C { static async Task M() { } }");

    // === POSITIVE (version 5): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void AsyncParamName_MemberAccess_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/AsyncParsingTests.cs:283
        // (MethodAsyncVarAsync) — `static async void M(object async) { async.F(); }`. Shows `async`
        // as a method modifier (combined with `static`), as a PARAMETER NAME, and as a member-access
        // base (`async.F()`) — all valid because `async` is a contextual keyword (not reserved).
        AssertParsesV5("class C { static async void M(object async) { async.F(); } }");
    }

    [TestMethod]
    public void Await_ExpressionBodiedReturn_Succeeds()
    {
        // The await expression is a full Expression, so it is usable wherever an Expression is
        // (here, as a return value). The operand is an invocation.
        AssertParsesV5("class C { async Task M() { return await N(); } }");
    }

    // === POSITIVE (version 1–4): `async`/`await` as plain IDENTIFIERS (must stay green). ===
    // `async` and `await` are contextual keywords (NOT reserved), so they remain valid names at
    // every version. A field NAMED `async`/`await` parses at v1–v4 (and v5).

    [TestMethod]
    public void Async_AsFieldName_ParsesAtV1()
        => AssertParsesV1("class C { int async; }");

    [TestMethod]
    public void Async_AsFieldName_ParsesAtV4()
        => AssertParsesV4("class C { int async; }");

    [TestMethod]
    public void Await_AsFieldName_ParsesAtV1()
        => AssertParsesV1("class C { int await; }");

    [TestMethod]
    public void Await_AsFieldName_ParsesAtV4()
        => AssertParsesV4("class C { int await; }");

    // === NEGATIVE (version 4, version-purity): the `async` MODIFIER is CS5-only. ===
    // At v4 `async` is not a method modifier, so `async void M() { }` REJECTS (parsed as a `Type`
    // name `async`, then `void` (reserved) cannot be the method name — the T2.1.5 finding).
    [TestMethod]
    public void AsyncVoidMethod_RejectedAtV4()
        => AssertFailsV4("class C { async void M() { } }");

    // === DOCUMENT (version 4): the `await` EXPRESSION is CS5-only, but `await` is a plain name. ===
    // At v4 the `AwaitExpr` alternative is absent. A bare `await x;` parses as a local declaration of
    // type `await` named `x` (valid C# 1.0 — the T2.1.5 D2 finding, the non-strict version-purity
    // analogous to `var x = 1;` at v1 / `dynamic x = 1;` at v3). This PARSES at v4 (documented, not
    // a reject).
    [TestMethod]
    public void Await_AsLocalDeclaration_ParsesAtV4()
        => AssertParsesV4("class C { void M() { await x; } }");

    // `await N();` (the await form with an invocation operand) REJECTS at v4: the `AwaitExpr`
    // alternative is absent, so `await` is read as a name — as a local-declaration type (`await N`
    // then fails on the `()`) or as a bare identifier expression (`await` then fails on the following
    // `N`). This is the version-purity discriminator for the await EXPRESSION (cf. T2.1.5 D2, which
    // used `return await x;` to force the expression reading; here the `()` plays the same role).
    [TestMethod]
    public void AwaitInvocation_RejectedAtV4()
        => AssertFailsV4("class C { void M() { await N(); } }");

    // === NEGATIVE (version 5, malformed): verify + document. ===

    // `await;` (await without an expression) PARSES at v5 as a bare identifier `await` followed by
    // `;` (an expression statement) — `await` is NOT reserved, so it is a valid IdentifierName and the
    // `AwaitExpr` alternative fails (no operand). This is the non-strict contextual-keyword behavior
    // (a documented deviation from Roslyn, where `await;` in an async method is a parse error because
    // Roslyn commits to the await reading in async context — IsAwaitExpression, LanguageParser.cs:11376).
    // Covered as a POSITIVE (documents the actual behavior), not a reject.
    [TestMethod]
    public void Await_NoOperand_ParsesAsBareIdentifierAtV5()
        => AssertParsesV5("class C { async void M() { await; } }");

    // `async N();` (async in an EXPRESSION context) REJECTS at v5: `async` is not reserved, so it is a
    // valid IdentifierName, but `async N()` is two identifiers in a row with no operator — not a valid
    // Expression. (`async` is only a METHOD MODIFIER, never an expression operator.)
    [TestMethod]
    public void AsyncInExpressionContext_RejectedAtV5()
        => AssertFailsV5("class C { void M() { async N(); } }");

    // === helpers ===

    private static void AssertParsesV5(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(5), input);

    private static void AssertFailsV5(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(5), input);

    private static void AssertParsesV4(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(4), input);

    private static void AssertFailsV4(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(4), input);

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
