#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// 5b.4.2 (A5-1, basic): E2E — S2 resync driven by a DERIVED probe predicate (no author anchors).
//
// The grammar declares NO author Anchors and NO author CanStart (no RecoveryOptions at all), so
// any S2 resync must come from DeriveProbePredicates. The NEW 5b.4.2 capability exercised here is
// the TOP-FRAME Ref alternative as a T1 anchor (before 5b.4.2 only loop-body Refs —
// DeriveLoopAnchors — were derived):
//
//   Module = ZeroOrMany(Stmt)
//   Stmt   = "let" Expr | Expr          ← top-frame Ref alternative: Expr (new in 5b.4.2)
//   Expr   = "var" ident ";"
//
// Input "var a; ### var b;": the main parse fails at e=7 — the furthest mismatch is Literal("let")
// (Stmt alt 0, Seq element 0), so the snapshot top frame is ("Stmt", SeqFrameLocation(0)).
// DeriveProbePredicates → [Expr (top-frame Ref alt), Stmt (loop-body Ref, the pre-5b.4.2 anchor)].
// The S2 scan finds Expr at 11 (full speculative parse of "var b;") → T1 resync @11 with the
// "let" absorber over [7..11); the re-parse continues alt 0 from 11 and reaches EOF in ONE
// accepted S2 candidate (no S6 floor, no Unrecovered).
[TerminalMatcher]
public sealed partial class DerivedProbePredicateTerminals
{
    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

[TestClass]
public sealed class DerivedProbePredicateTests
{
    private static string Describe(Parser parser)
        => string.Join("; ", parser.RecoveryDiagnostics.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) «{d.Message}» rule={d.RuleName ?? "-"}"));

    private static Parser NewParser()
    {
        var parser = new Parser(DerivedProbePredicateTerminals.Trivia());
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Stmt"), "Stmts")];
        parser.Rules["Stmt"] =
        [
            new Seq([new Literal("let"), new Ref("Expr")], "LetExpr"),
            new Ref("Expr"),
        ];
        parser.Rules["Expr"] =
        [
            new Seq([new Literal("var"), DerivedProbePredicateTerminals.Ident(), new Literal(";")], "VarDecl"),
        ];
        parser.BuildTdoppRules();
        return parser;
    }

    // The S2 resync is driven by "Expr" — the top-frame Ref alternative of Stmt, derived by
    // DeriveProbePredicates (5b.4.2). Neither "Expr" nor "Stmt" is author-declared (no Anchors /
    // CanStart anywhere in the grammar); "Stmt" (the loop-body anchor) is the pre-5b.4.2 driver
    // and must NOT be the one used.
    [TestMethod]
    public void Test_S2_Resync_Driven_By_Derived_TopFrameRef_Alternative()
    {
        var parser = NewParser();
        var input = "var a; ### var b;";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == input.Length,
            $"Expected Success@EOF via S2 resync, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} diag={Describe(parser)}");
        Assert.IsNull(parser.ErrorInfo,
            $"Expected ErrorInfo null (recovered), got ErrorInfo@{parser.ErrorInfo?.Pos}. diag={Describe(parser)}");

        // The S2 resync diagnostic is present (a Skipped "resync point" — not an S6 floor / Unrecovered outcome).
        var resync = parser.RecoveryDiagnostics.Single(d => d.Kind == RecoveryKind.Skipped && d.Message.Contains("resync point"));
        Assert.AreEqual(7, resync.StartPos);
        Assert.AreEqual(10, resync.EndPos);
        Assert.IsTrue(resync.Message.Contains("resync point 11"), $"unexpected resync message: {resync.Message}");

        // Driving anchor = "Expr": a derived (non-author) top-frame Ref alternative — the new 5b.4.2
        // T1 capability. The pre-5b.4.2 loop-body anchor "Stmt" never drove the resync.
        Assert.AreEqual(1, parser.GrammarDiagnostics.AnchorUsage("Expr"),
            $"the top-frame Ref alternative must be the driving anchor; used=[{string.Join(",", parser.GrammarDiagnostics.UsedAnchors)}] diags={Describe(parser)}");
        Assert.AreEqual(0, parser.GrammarDiagnostics.AnchorUsage("Stmt"), "the loop-body anchor must not drive the resync");
        Assert.AreEqual(1, parser.GrammarDiagnostics.UsedAnchors.Count, "exactly one anchor drove the S2 resync");
    }

    // The derivation on the REAL failure snapshot: top-frame Ref alternatives first (Expr), then
    // loop-body Refs (Stmt). "Expr" is the predicate that did not exist before 5b.4.2.
    [TestMethod]
    public void Test_DeriveProbePredicates_TopFrameRefAlt_Before_LoopAnchor()
    {
        var parser = NewParser();
        parser.MaxRecoveryIterations = 0;
        parser.Parse("var a; ### var b;", "Module", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        Assert.AreEqual(7, snapshot!.Pos);

        var top = snapshot.Stack[^1];
        Assert.AreEqual("Stmt", top.RuleName);
        Assert.IsTrue(top.Location is SeqFrameLocation { ElementIndex: 0 }, $"unexpected top frame location: {top.Location}");

        var names = RecoveryEngine.DeriveProbePredicates(parser, snapshot).Select(r => r.RuleName).ToArray();
        CollectionAssert.AreEqual(new[] { "Expr", "Stmt" }, names);
    }

    // Control: a valid input (same grammar) parses cleanly to EOF with NO recovery at all.
    [TestMethod]
    public void Test_Valid_Input_Parses_Cleanly_Without_Recovery()
    {
        var parser = NewParser();
        var input = "var a; var b;";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == input.Length,
            $"Expected clean Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length}");
        Assert.IsNull(parser.ErrorInfo, "a clean parse must not set ErrorInfo");
        Assert.AreEqual(0, parser.RecoveryDiagnostics.Count, "no recovery diagnostics expected on a clean parse");
        Assert.AreEqual(0, parser.GrammarDiagnostics.AnchorUsage("Expr"), "no anchor usage expected on a clean parse");
        Assert.AreEqual(0, parser.GrammarDiagnostics.AnchorUsage("Stmt"), "no anchor usage expected on a clean parse");
    }
}
