#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// A5-2 (6.1.2b): recovery strategies register their diagnostics in the side-table so that
// DeriveRecoveryDiagnostics(root) reproduces the accumulated list's KIND (D1) and Terminal/RuleName
// metadata (D4). Mechanism B (Parser.Recovery.cs FinishRecovery): at session end, each accumulated
// diagnostic is matched to the node it corresponds to in the FINAL tree by position+shape and the
// EXACT same RecoveryDiagnostic instance is attached — so derive returns the accumulated
// diagnostics themselves. The public RecoveryDiagnostics list is UNCHANGED (6.1.2c); soft-separator
// (D2) is out of scope (6.1.2b2).
[TestClass]
public sealed class SideTableEngineTests
{
    // ============ D1: S1b single-token deletion — derive returns Extraneous (NOT Skipped) at the absorber's position ============

    // Grammar from SingleTokenDeletionTests: S := 'a' 'b', input "aab". S1b absorbs the extraneous
    // 'a' at [1..2) (absorber node, IsRecovery + IsAbsorber), satisfying the 'b' slot; the real 'b'@2
    // is then trailing garbage — the S6 floor absorbs it at [2..3) and adds the Unrecovered marker
    // at e=2 (a zero marker, NOT a tree node — A4-2). The accumulated list therefore holds
    // Extraneous [1..2) + Skipped [2..3) + Unrecovered [2..2); derive must return the first two as
    // the EXACT accumulated instances — kind Extraneous for the S1b absorber, not Skipped (the pure
    // structural derivation of 6.1.1 would call this tree node Skipped).
    [TestMethod]
    public void Test_D1_SingleTokenDeletion_DerivedIsExtraneousWithMetadata()
    {
        var parser = new Parser(SingleTokenDeletionTerminals.Trivia());
        parser.Rules["S"] = [new Seq([new Literal("a"), new Literal("b")], "S")];
        parser.BuildTdoppRules();

        var input = "aab";
        var result = parser.Parse(input, "S", out _);
        if (!result.TryGetSuccess(out var root, out var end) || end != input.Length || root is null)
            Assert.Fail($"expected Success@EOF (end=3), got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos}");
        Assert.IsNull(parser.ErrorInfo);

        var accumulated = parser.RecoveryDiagnostics;
        Assert.AreEqual(3, accumulated.Count, $"expected the S1b + S6 + Unrecovered diagnostics, got: {Describe(accumulated)}");
        var s1b = accumulated[0];
        Assert.AreEqual(RecoveryKind.Extraneous, s1b.Kind, Describe(accumulated));
        Assert.AreEqual(1, s1b.StartPos, Describe(accumulated));
        Assert.AreEqual(2, s1b.EndPos, Describe(accumulated));

        var derived = parser.DeriveRecoveryDiagnostics(root);
        // The Unrecovered marker has no tree node — derived = the S1b + S6 diagnostics (2 of the 3).
        Assert.AreEqual(2, derived.Count, $"expected S1b + S6 derived, got: {Describe(derived)}");

        // D1: the S1b absorber [1..2) derives as Extraneous (not Skipped), the exact accumulated instance.
        var d1 = derived[0];
        Assert.IsTrue(ReferenceEquals(s1b, d1), $"derived[0] must be the exact S1b instance, got: {Describe(derived)}");
        Assert.AreEqual(RecoveryKind.Extraneous, d1.Kind, $"D1: S1b must derive as Extraneous, not Skipped, got: {Describe(derived)}");
        Assert.AreEqual(1, d1.StartPos, Describe(derived));
        Assert.AreEqual(2, d1.EndPos, Describe(derived));
        Assert.IsNotNull(d1.Terminal, $"D4: Terminal must be present, got: {Describe(derived)}");
        Assert.AreEqual("b", d1.Terminal!.Kind, Describe(derived));
        Assert.AreEqual("S", d1.RuleName, Describe(derived));
        Assert.IsFalse(derived.Any(d => d.StartPos == 1 && d.EndPos == 2 && d.Kind == RecoveryKind.Skipped),
            $"no Skipped may cover the S1b absorber span, got: {Describe(derived)}");

        // The S6 floor's absorber [2..3) also derives, as the exact accumulated instance.
        var d2 = derived[1];
        Assert.IsTrue(ReferenceEquals(accumulated[1], d2), $"derived[1] must be the exact S6 instance, got: {Describe(derived)}");
        Assert.AreEqual(RecoveryKind.Skipped, d2.Kind, Describe(derived));
        Assert.AreEqual(2, d2.StartPos, Describe(derived));
        Assert.AreEqual(3, d2.EndPos, Describe(derived));
    }

    // ============ D4: S1 insertion — derived diagnostic carries non-null Terminal/RuleName ============

    // Grammar from IterativeRecoveryTests, input "int f ) { int x; }" (missing '('). S0 cannot
    // repair it; S1 inserts '(' at e=6 (zero-width node, Kind "("). The accumulated list holds
    // Inserted [6..6) term=( rule=Function (the top frame's rule name); derive must return it with
    // Terminal/RuleName intact.
    [TestMethod]
    public void Test_D4_S1Insertion_DerivedCarriesTerminalAndRuleName()
    {
        var parser = NewModuleParser();
        var input = "int f ) { int x; }";
        var result = parser.Parse(input, "Module", out _);
        if (!result.TryGetSuccess(out var root, out var end) || end != input.Length || root is null)
            Assert.Fail($"expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos}");
        Assert.IsNull(parser.ErrorInfo);

        var accumulated = parser.RecoveryDiagnostics;
        Assert.AreEqual(1, accumulated.Count, $"expected exactly the S1 diagnostic, got: {Describe(accumulated)}");
        var acc = accumulated[0];
        Assert.AreEqual(RecoveryKind.Inserted, acc.Kind, Describe(accumulated));

        var derived = parser.DeriveRecoveryDiagnostics(root);
        Assert.AreEqual(1, derived.Count, $"expected exactly 1 derived diagnostic, got: {Describe(derived)}");
        var d = derived[0];
        Assert.IsTrue(ReferenceEquals(acc, d), $"derived must be the exact accumulated instance, got: {Describe(derived)}");
        Assert.AreEqual(RecoveryKind.Inserted, d.Kind, Describe(derived));
        Assert.AreEqual(6, d.StartPos, Describe(derived));
        Assert.AreEqual(6, d.EndPos, Describe(derived));
        Assert.IsNotNull(d.Terminal, $"D4: Terminal must be non-null, got: {Describe(derived)}");
        Assert.AreEqual("(", d.Terminal!.Kind, Describe(derived));
        Assert.IsNotNull(d.RuleName, $"D4: RuleName must be non-null, got: {Describe(derived)}");
        Assert.AreEqual("Function", d.RuleName, Describe(derived));
    }

    // ============ Iterative recovery: two S1 insertions across two iterations — both derived ============

    // "int f ) { int x; } int g ) { int y; }": iteration 1 accepts S1 at e=6 (f's '('), progress;
    // iteration 2 accepts S1 at e=25 (g's '('), Success@EOF. The session-end match must attach BOTH
    // diagnostics to the final tree's nodes (f's node instance survives via the memo, g's is fresh)
    // — derive returns both, in position order, as the exact accumulated instances.
    [TestMethod]
    public void Test_Iterative_TwoInsertions_BothDerived()
    {
        var parser = NewModuleParser();
        var input = "int f ) { int x; } int g ) { int y; }";
        var result = parser.Parse(input, "Module", out _);
        if (!result.TryGetSuccess(out var root, out var end) || end != input.Length || root is null)
            Assert.Fail($"expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos}");
        Assert.IsNull(parser.ErrorInfo);

        var accumulated = parser.RecoveryDiagnostics;
        Assert.AreEqual(2, accumulated.Count, $"expected the two S1 diagnostics, got: {Describe(accumulated)}");

        var derived = parser.DeriveRecoveryDiagnostics(root);
        Assert.AreEqual(2, derived.Count, $"expected exactly 2 derived diagnostics, got: {Describe(derived)}");
        for (var i = 0; i < 2; i++)
        {
            Assert.IsTrue(ReferenceEquals(accumulated[i], derived[i]),
                $"derived[{i}] must be the exact accumulated instance, got: {Describe(derived)}");
            Assert.AreEqual(RecoveryKind.Inserted, derived[i].Kind, Describe(derived));
            Assert.IsNotNull(derived[i].Terminal, Describe(derived));
            Assert.AreEqual("(", derived[i].Terminal!.Kind, Describe(derived));
            Assert.AreEqual("Function", derived[i].RuleName, Describe(derived));
        }
        Assert.IsTrue(derived[0].StartPos < derived[1].StartPos, Describe(derived));
        Assert.AreEqual(6, derived[0].StartPos, Describe(derived));
    }

    // Grammar from IterativeRecoveryTests (a missing '(' has no OftenMissed — repaired by S1 insertion).
    private static Parser NewModuleParser()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());
        var closingBrace = new OftenMissed(new Literal("}"));
        var semicolon = new OftenMissed(new Literal(";"));

        parser.Rules["Expr"] = new Rule[]
        {
            RecoveryTerminals.Number(),
            RecoveryTerminals.Ident(),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
            new Seq([new Ref("Expr"), new Literal("="), new ReqRef("Expr", 10, Right: true)], "Assignment"),
            new Seq([new Ref("Expr"), RecoveryTerminals.ErrorOperator(), new ReqRef("Expr", 200)], "RecoveryOperator"),
            new Seq([new Ref("Expr"), RecoveryTerminals.ErrorEmpty(), new ReqRef("Expr", 200)], "RecoveryEmptyOperator"),
            RecoveryTerminals.ErrorEmpty(),
        };

        parser.Rules["Statement"] = new Rule[]
        {
            new Seq([new Literal("int"), RecoveryTerminals.Ident(), semicolon], "VarDecl"),
            new Seq([new Ref("Expr"), semicolon], "ExprStmt"),
        };

        parser.Rules["Block"] = new Rule[]
        {
            new Seq([new Literal("{"), new ZeroOrMany(new Seq([new NotPredicate(new Ref("Function")), new Ref("Statement")], "BlockStatement")), closingBrace], "MultiBlock"),
            new Ref("Statement", "SimplBlock"),
        };

        parser.Rules["Function"] = new Rule[]
        {
            new Seq([new Literal("int"), RecoveryTerminals.Ident(), new Literal("("), new Literal(")"), new Ref("Block")], "FunctionDecl"),
        };

        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Function"), "ModuleFunctions")];

        parser.BuildTdoppRules();
        return parser;
    }

    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) \"{d.Message}\" term={d.Terminal?.Kind} rule={d.RuleName}"));
}
