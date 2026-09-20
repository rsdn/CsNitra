#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// B4.3: RecoveryEngine consumes the level-aware effective values (EffectiveStrategyMask,
// EffectiveMaxSkip, SpeculationEnabled). These tests call RecoveryEngine.Generate directly
// (not via Parse) so DegradationLevel / Profile are not reset by the recovery loop, and
// observe the effect on the generated candidate set / scan counters.
[TestClass]
public sealed class DegradationGatingTests
{
    private sealed record SpaceTrivia(string Name) : Terminal(Name)
    {
        public override int TryMatch(string input, int startPos)
        {
            var len = 0;
            while (startPos + len < input.Length && input[startPos + len] == ' ')
                len++;
            return len;
        }
    }

    // Start = "a" Body "b", Body = "{" Item* "}", Item = "i" "c".
    private static Parser NewBracedParser()
    {
        var parser = new Parser(new SpaceTrivia("Trivia"));
        parser.MaxRecoveryIterations = 0;
        parser.Rules["Start"] = [new Seq([new Literal("a"), new Ref("Body"), new Literal("b")], "Start")];
        parser.Rules["Body"] = [new Seq([new Literal("{"), new ZeroOrMany(new Ref("Item")), new Literal("}")], "Body")];
        parser.Rules["Item"] = [new Seq([new Literal("i"), new Literal("c")], "Item")];
        parser.BuildTdoppRules();
        return parser;
    }

    // Start = "a" Body "b", Body = "{" Item* "}", Item = "i" (single char).
    private static Parser NewMaxSkipParser()
    {
        var parser = new Parser(new SpaceTrivia("Trivia"));
        parser.MaxRecoveryIterations = 0;
        parser.Rules["Start"] = [new Seq([new Literal("a"), new Ref("Body"), new Literal("b")], "Start")];
        parser.Rules["Body"] = [new Seq([new Literal("{"), new ZeroOrMany(new Ref("Item")), new Literal("}")], "Body")];
        parser.Rules["Item"] = [new Seq([new Literal("i")], "Item")];
        parser.BuildTdoppRules();
        return parser;
    }

    // Start = "a" "b".
    private static Parser NewSimpleParser()
    {
        var parser = new Parser(new SpaceTrivia("Trivia"));
        parser.MaxRecoveryIterations = 0;
        parser.Rules["Start"] = [new Seq([new Literal("a"), new Literal("b")], "Start")];
        parser.BuildTdoppRules();
        return parser;
    }

    // B4.3 mask gating: with EffectiveStrategyMask = S1|S6, the disabled strategies (S2/S3/S4/S5)
    // are not generated; S1 and the guaranteed floor S6 remain. The scenario's baseline (mask = All)
    // generates S3/S4, so the mask provably shrinks the candidate set.
    [TestMethod]
    public void Test_Mask_Gating_Suppresses_Disabled_Strategies()
    {
        var parser = NewBracedParser();
        var input = "a { x { y } } b";
        parser.Parse(input, "Start", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;

        var baseline = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure, "Start", 0, e);
        Assert.IsTrue(baseline.Any(c => c.Rank is 3 or 4),
            $"Baseline (mask=All) must generate S3/S4, got ranks [{string.Join(",", baseline.Select(c => c.Rank))}]");

        parser.Profile = new RecoveryProfile { StrategyMask = RecoveryStrategy.S1 | RecoveryStrategy.S6 };
        var masked = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure, "Start", 0, e);

        Assert.IsTrue(masked.All(c => c.Rank is 1 or 6),
            $"Masked set must only contain S1/S6, got ranks [{string.Join(",", masked.Select(c => c.Rank))}]");
        Assert.IsTrue(masked.Any(c => c.Rank == 1), "S1 must still be generated");
        Assert.IsTrue(masked.Any(c => c.Rank == 6), "S6 must still be generated (guaranteed floor)");
        Assert.IsTrue(masked.Count < baseline.Count,
            $"Masked set ({masked.Count}) must be smaller than baseline ({baseline.Count})");
    }

    // B4.3 mask gating (S5): S5 is only generated when the mask includes S5 AND the existing
    // condition (resultKind==Success && e<input.Length). With mask S1|S6, S5 is suppressed while
    // the floor S6 is still emitted.
    [TestMethod]
    public void Test_Mask_Gating_S5_Suppressed_When_Not_In_Mask()
    {
        var parser = NewSimpleParser();
        var input = "abQ";
        parser.Parse(input, "Start", out _);

        var e = 2;
        var frame = new StackFrame("Start", 0, new RuleFrameLocation(0), [EofTerminal.Instance], null);
        var snapshot = new FailureSnapshot(e, [frame], new Literal("a"), [EofTerminal.Instance]);

        var baseline = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Success, "Start", 0, e);
        Assert.IsTrue(baseline.Any(c => c.Rank == 5),
            $"Baseline (mask=All) must generate S5, got ranks [{string.Join(",", baseline.Select(c => c.Rank))}]");

        parser.Profile = new RecoveryProfile { StrategyMask = RecoveryStrategy.S1 | RecoveryStrategy.S6 };
        var masked = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Success, "Start", 0, e);

        Assert.IsFalse(masked.Any(c => c.Rank == 5), "S5 must be suppressed when not in the mask");
        Assert.IsTrue(masked.Any(c => c.Rank == 6), "S6 must still be generated (guaranteed floor)");
    }

    // B4.3 MaxSkip: Generate must fall back to EffectiveMaxSkip (not Profile.MaxSkip) when no frame
    // override is present. Level 1 expands the window x4, so the S3 panic-mode scan covers 4x the
    // positions (the x-run has no terminator, so the scan is bounded purely by the window).
    [TestMethod]
    public void Test_Generate_Uses_EffectiveMaxSkip()
    {
        var input = "a{" + new string('x', 5000);

        // Level 0: EffectiveMaxSkip = Profile.MaxSkip = 1000.
        var parser0 = NewMaxSkipParser();
        parser0.Parse(input, "Start", out _);
        var snapshot0 = parser0.LastSnapshot;
        Assert.IsNotNull(snapshot0);
        var e0 = snapshot0!.Pos;
        Assert.AreEqual(2, e0);
        RecoveryEngine.Generate(e0, snapshot0, input, parser0, Result.Kind.Failure, "Start", 0, e0);
        var scan0 = parser0.S3ScanPositions;

        // Level 1: EffectiveMaxSkip = Profile.MaxSkip * 4 = 4000.
        var parser1 = NewMaxSkipParser();
        parser1.Parse(input, "Start", out _);
        var snapshot1 = parser1.LastSnapshot;
        Assert.IsNotNull(snapshot1);
        var e1 = snapshot1!.Pos;
        parser1.DegradationLevel = 1;
        RecoveryEngine.Generate(e1, snapshot1, input, parser1, Result.Kind.Failure, "Start", 0, e1);
        var scan1 = parser1.S3ScanPositions;

        Assert.AreEqual(1000, scan0, $"Level 0 S3 scan must be bounded by MaxSkip=1000, got {scan0}");
        Assert.AreEqual(4000, scan1, $"Level 1 S3 scan must be bounded by EffectiveMaxSkip=4000, got {scan1}");
    }

    // B4.3 Speculation: when SpeculationEnabled is false (level >= 1), S2 must skip the expensive
    // per-position speculative re-parse. The speculative cache (hits+misses) is only touched by the
    // speculative parse, so it must stay at 0 when speculation is disabled and be > 0 when enabled.
    [TestMethod]
    public void Test_Generate_Speculation_Gated_By_SpeculationEnabled()
    {
        var input = "a { x i c } b";

        // Level 0: speculation enabled — S2 performs the speculative parse (anchor Item at 'i').
        var parser0 = NewBracedParser();
        parser0.Parse(input, "Start", out _);
        var snapshot0 = parser0.LastSnapshot;
        Assert.IsNotNull(snapshot0);
        var e0 = snapshot0!.Pos;
        RecoveryEngine.Generate(e0, snapshot0, input, parser0, Result.Kind.Failure, "Start", 0, e0);
        var spec0 = parser0.SpecCacheHits + parser0.SpecCacheMisses;

        // Level 1: speculation disabled — S2 skips the speculative parse (First-scan only).
        var parser1 = NewBracedParser();
        parser1.Parse(input, "Start", out _);
        var snapshot1 = parser1.LastSnapshot;
        Assert.IsNotNull(snapshot1);
        var e1 = snapshot1!.Pos;
        parser1.DegradationLevel = 1;
        RecoveryEngine.Generate(e1, snapshot1, input, parser1, Result.Kind.Failure, "Start", 0, e1);
        var spec1 = parser1.SpecCacheHits + parser1.SpecCacheMisses;

        Assert.IsTrue(spec0 > 0, $"Level 0 must perform speculative parsing, got {spec0}");
        Assert.AreEqual(0, spec1, $"Level 1 must skip speculative parsing, got {spec1}");
    }
}
