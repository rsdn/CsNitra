#nullable enable

using System.Diagnostics;
using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TerminalMatcher]
public sealed partial class RecoveryMetricsTerminals
{
    [Regex(@"\d+")]
    public static partial Terminal Number();

    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

// D2-full (6.2.1/6.2.2): RecoveryMetrics — accept/rollback per strategy S0..S6 + specCache hits/misses
// (6.2.1), time-by-phase + hygiene removals + braking-scenario acceptance test (6.2.2).
// Grammar mirrors SpecCacheSharedTests (Module/Stmt/Expr, TDOPP Expr) so S2 resync drives the shared
// specCache (First(Stmt) = {Ident} → Speculative at each Ident after the garbage). Stateless: each
// test builds its own Parser (method-level parallel execution).
[TestClass]
public sealed class RecoveryMetricsTests
{
    private static Parser NewParser()
    {
        var parser = new Parser(RecoveryMetricsTerminals.Trivia());
        parser.Rules["Expr"] = new Rule[]
        {
            RecoveryMetricsTerminals.Number(),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
            new Seq([new Ref("Expr"), new Literal("-"), new ReqRef("Expr", 100)], "Sub"),
            new Seq([new Ref("Expr"), new Literal("*"), new ReqRef("Expr", 200)], "Mul"),
            new Seq([new Ref("Expr"), new Literal("/"), new ReqRef("Expr", 200)], "Div"),
            new Seq([new Ref("Expr"), new Literal("=="), new ReqRef("Expr", 50)], "Eq"),
            new Seq([new Ref("Expr"), new Literal("!="), new ReqRef("Expr", 50)], "Neq"),
        };
        parser.Rules["Stmt"] = [new Seq([RecoveryMetricsTerminals.Ident(), new Literal(":"), new Ref("Expr"), new Literal(";")], "Stmt")];
        parser.Rules["Module"] = [new Seq([new Literal("{"), new ZeroOrMany(new Ref("Stmt"), "Stmts"), new Literal("}")], "Module")];
        parser.BuildTdoppRules();
        return parser;
    }

    private static string Describe(RecoveryMetrics m) =>
        $"accept(S0..S6)=[{m.AcceptCount(0)},{m.AcceptCount(1)},{m.AcceptCount(2)},{m.AcceptCount(3)},{m.AcceptCount(4)},{m.AcceptCount(5)},{m.AcceptCount(6)}] " +
        $"rollback(S0..S6)=[{m.RollbackCount(0)},{m.RollbackCount(1)},{m.RollbackCount(2)},{m.RollbackCount(3)},{m.RollbackCount(4)},{m.RollbackCount(5)},{m.RollbackCount(6)}] " +
        $"specHits={m.SpecCacheHits} specMisses={m.SpecCacheMisses}";

    // Two errors → two recovery iterations. Each is followed by an Ident, so S2's speculative parse
    // drives the shared specCache; S3 (panic to ';') is the repair (accepted once per error). S0 and
    // the S1 insert candidates are tried first and rolled back. The (Stmt,'d') lookup is cached across
    // the two iterations (the cache lives for the whole Recover, not per iteration) → at least one hit.
    [TestMethod]
    public void Test_MultiIteration_AcceptRollbackPerStrategy_AndSpecCache()
    {
        var parser = NewParser();
        var input = "{ a: 1+ ### b; c: 2+ ### d; }";
        var result = parser.Parse(input, "Module", out _);
        var m = parser.Metrics;
        Trace.WriteLine(Describe(m) + $" passes={parser.RecoveryPasses}");

        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == input.Length,
            $"Expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos}\n{Describe(m)}");

        // Accept: S3 (panic) is the repair, accepted once per error (two iterations).
        Assert.IsTrue(m.TotalAccept >= 1, $"Expected TotalAccept >= 1, got {m.TotalAccept}\n{Describe(m)}");
        Assert.IsTrue(m.AcceptCount(RecoveryStrategy.S3) >= 1,
            $"Expected AcceptCount(S3) >= 1, got {m.AcceptCount(RecoveryStrategy.S3)}\n{Describe(m)}");

        // Rollback: S0 (and the S1 insert candidates) are tried and rolled back before S3 is accepted.
        Assert.IsTrue(m.TotalRollback >= 1, $"Expected TotalRollback >= 1, got {m.TotalRollback}\n{Describe(m)}");

        // specCache: S2 resync speculatively parses Stmt at the Idents after the garbage → misses; the
        // (Stmt,'d') lookup is cached across the two iterations → at least one hit.
        Assert.IsTrue(m.SpecCacheMisses > 0, $"Expected SpecCacheMisses > 0, got {m.SpecCacheMisses}\n{Describe(m)}");
        Assert.IsTrue(m.SpecCacheHits > 0, $"Expected SpecCacheHits > 0 (cache lives per-Recover), got {m.SpecCacheHits}\n{Describe(m)}");

        // Consistency: per-strategy counts sum to the totals; total accepts <= recovery passes.
        var acceptSum = m.AcceptCount(RecoveryStrategy.S0) + m.AcceptCount(RecoveryStrategy.S1) + m.AcceptCount(RecoveryStrategy.S2)
            + m.AcceptCount(RecoveryStrategy.S3) + m.AcceptCount(RecoveryStrategy.S4) + m.AcceptCount(RecoveryStrategy.S5)
            + m.AcceptCount(RecoveryStrategy.S6);
        Assert.AreEqual(m.TotalAccept, acceptSum);
        Assert.IsTrue(m.TotalAccept <= parser.RecoveryPasses,
            $"Expected TotalAccept ({m.TotalAccept}) <= RecoveryPasses ({parser.RecoveryPasses})");

        // The metrics' specCache counters agree with the Parser's existing D2-core surface.
        Assert.AreEqual(parser.SpecCacheMisses, m.SpecCacheMisses);
        Assert.AreEqual(parser.SpecCacheHits, m.SpecCacheHits);
    }

    // D2-full (6.2.2) braking-scenario acceptance test: many errors (D1.1-style) stress the recovery
    // loop — the "braking" must be VISIBLE in the metrics (non-zero phase times + hygiene removals),
    // not a timeout. Each error → one iteration: a main-loop re-parse (iter >= 1), a Generate call
    // (S0 re-parse makes no progress → rolled back), and the candidate re-parses until S3 (panic) is
    // accepted; HygieneCore runs in every ApplyPatches and removes the invalid Failure / stale
    // start-rule records.
    [TestMethod]
    public void Test_BrakingScenario_ManyErrors_PhaseTimesAndHygieneVisible()
    {
        var parser = NewParser();
        var input = "{ a: 1+ ### b; c: 2+ ### d; e: 3+ ### f; g: 4+ ### h; }";
        var result = parser.Parse(input, "Module", out _);
        var m = parser.Metrics;
        var msg = Describe(m)
            + $" main={m.MainParseTime.TotalMilliseconds:F3}ms gen={m.GenerationTime.TotalMilliseconds:F3}ms "
            + $"reparse={m.ReparseTime.TotalMilliseconds:F3}ms hygieneAfter={m.HygieneRemovalsAfter}";

        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == input.Length,
            $"Expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos}\n{msg}");

        // (a) the initial parse ran.
        Assert.IsTrue(m.MainParseTime > TimeSpan.Zero, $"Expected MainParseTime > 0\n{msg}");
        // (b) generation ran: S0 failed at each of the four errors → a Generate call per iteration.
        Assert.IsTrue(parser.EngineGenerateCalls > 0,
            $"Expected EngineGenerateCalls > 0, got {parser.EngineGenerateCalls}\n{msg}");
        Assert.IsTrue(m.GenerationTime > TimeSpan.Zero, $"Expected GenerationTime > 0\n{msg}");
        // (c) re-parses ran: the iterative main-loop passes (iter >= 1) + every candidate re-parse.
        Assert.IsTrue(m.ReparseTime > TimeSpan.Zero, $"Expected ReparseTime > 0\n{msg}");
        // Hygiene ran (ApplyPatches on every candidate) and removed memo entries.
        Assert.IsTrue(m.HygieneRemovalsAfter > 0, $"Expected HygieneRemovalsAfter > 0\n{msg}");
        Assert.AreEqual(parser.HygieneRemovals, m.HygieneRemovalsAfter,
            "metrics agree with the D2-core HygieneRemovals hook");
    }

    // Metrics are per-Parse: a clean parse leaves no accept/rollback; the next (dirty) parse resets
    // and accumulates fresh (the session-start reset in Recover).
    [TestMethod]
    public void Test_Metrics_Reset_BetweenParses()
    {
        var parser = NewParser();

        var clean = parser.Parse("{ a: 1; }", "Module", out _);
        Assert.IsTrue(clean.TryGetSuccess(out _, out var c) && c == "{ a: 1; }".Length);
        Assert.AreEqual(0, parser.Metrics.TotalAccept, "clean parse: no recovery");
        Assert.AreEqual(0, parser.Metrics.TotalRollback, "clean parse: no recovery");

        var dirty = parser.Parse("{ a: 1+ ### b; }", "Module", out _);
        Assert.IsTrue(dirty.TryGetSuccess(out _, out var d) && d == "{ a: 1+ ### b; }".Length);
        Assert.IsTrue(parser.Metrics.TotalAccept >= 1, $"dirty parse: expected recovery, got {parser.Metrics.TotalAccept}");
    }
}
