#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// A4-6 (6.3.1): GrammarDiagnostics — the grammar-quality (author-feedback) channel. These tests assert
// the POSITIVE collection works: an author anchor that is used gets a non-zero usage count, a declared
// anchor that is never used stays at 0, a Recoverable=false region that fires gets a firing count, and
// a recovered rule gets a distinct recovery point. The NEGATIVE corpus-level signals (never-used over a
// whole corpus, never-fires, always-the-same S2 anchor) are 6.3.2 — they need a corpus run. Stateless:
// each test builds its own Parser (method-level parallel execution).
[TerminalMatcher]
public sealed partial class GrammarDiagTerminals
{
    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

[TestClass]
public sealed class GrammarDiagnosticsTests
{
    // ============ Signal 1 + 4 (anchor usage) + Signal 2 (recovery points) ============

    // Author anchors: Item (used — the resync target) and ItemAlt (declared but never used — the input
    // has no "alt" token, so its First never matches). S2 resync to the next Item drives the recovery.
    private static Parser NewAnchorParser()
    {
        var parser = new Parser(GrammarDiagTerminals.Trivia());
        parser.MaxRecoveryAttemptsPerPosition = 16;
        parser.Rules["Item"] =
        [
            new Seq([new Literal("int"), GrammarDiagTerminals.Ident(), new Literal(";")], "Item"),
        ];
        parser.Rules["ItemAlt"] =
        [
            new Seq([new Literal("alt"), GrammarDiagTerminals.Ident(), new Literal(";")], "ItemAlt"),
        ];
        parser.Rules["Module"] =
        [
            new RecoveryRule(new ZeroOrMany(new Ref("Item"), "Items"), new RecoveryOptions { Anchors = [new Ref("Item"), new Ref("ItemAlt")] }),
        ];
        parser.BuildTdoppRules();
        return parser;
    }

    [TestMethod]
    public void Test_AnchorUsage_UsedAndNeverUsed_Collected()
    {
        var parser = NewAnchorParser();
        var input = "int a; ### int b;";
        var result = parser.Parse(input, "Module", out _);
        var gd = parser.GrammarDiagnostics;

        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == input.Length,
            $"Expected Success@EOF via S2 resync, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos}");

        // The used anchor (Item — the resync target) has a non-zero usage count.
        Assert.IsTrue(gd.AnchorUsage("Item") >= 1, $"Expected AnchorUsage(Item) >= 1, got {gd.AnchorUsage("Item")}");
        Assert.IsTrue(gd.UsedAnchors.Contains("Item"), "Expected UsedAnchors to contain the used anchor Item");

        // The declared-but-never-used anchor (ItemAlt) stays at 0 and is not in the used set.
        Assert.AreEqual(0, gd.AnchorUsage("ItemAlt"), "ItemAlt is never a resync target → usage must stay 0");
        Assert.IsFalse(gd.UsedAnchors.Contains("ItemAlt"), "ItemAlt was never used → it must not be in UsedAnchors");

        // Signal 2: the recovery (S2 resync) recorded a distinct recovery point for some rule.
        Assert.IsTrue(gd.TotalDistinctRecoveryPoints >= 1,
            $"Expected TotalDistinctRecoveryPoints >= 1, got {gd.TotalDistinctRecoveryPoints}");
        Assert.IsTrue(gd.RecoveredRules.Count >= 1, "Expected at least one recovered rule");
    }

    // ============ Signal 3 (Recoverable=false region firing) ============

    // S = a STRICT b, STRICT = RecoveryRule(x, Recoverable=false). Input "acb": STRICT expects x but
    // finds c → a failure inside the strict region. S0 makes no progress → Generate detects strict →
    // S1..S5 suppressed, only the S6 bottom is emitted (reaches EOF). The strict region fired once.
    [TestMethod]
    public void Test_StrictRegionFiring_Collected()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));
        parser.Rules["S"] =
        [
            new Seq([new Literal("a"), new Ref("STRICT"), new Literal("b")], "S"),
        ];
        parser.Rules["STRICT"] =
        [
            new RecoveryRule(new Seq([new Literal("x")], "STRICT"), new RecoveryOptions { Recoverable = false }),
        ];
        parser.BuildTdoppRules();

        var input = "acb";
        var result = parser.Parse(input, "S", out _);
        var gd = parser.GrammarDiagnostics;

        // The strict region (Recoverable=false) fired: S1..S5 were suppressed at the failure inside it.
        Assert.IsTrue(gd.StrictRegionFirings("STRICT") >= 1,
            $"Expected StrictRegionFirings(STRICT) >= 1, got {gd.StrictRegionFirings("STRICT")}");
        Assert.IsTrue(gd.StrictRegionFiringsAll.ContainsKey("STRICT"),
            "Expected STRICT to be present in the strict-region firing map");

        // The strict region still reaches EOF via the guaranteed S6 bottom (C1: strict ⇒ only S6).
        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == input.Length,
            $"Expected Success@EOF via the S6 bottom, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos}");
    }

    // ============ Accumulation + Reset ============

    // GrammarDiagnostics accumulates across parses (corpus-level) and Reset() clears it. Two parses of
    // the same dirty input double the anchor-usage count; Reset() returns it to 0.
    [TestMethod]
    public void Test_AccumulatesAcrossParses_AndReset()
    {
        var parser = NewAnchorParser();
        const string input = "int a; ### int b;";

        parser.Parse(input, "Module", out _);
        var afterFirst = parser.GrammarDiagnostics.AnchorUsage("Item");
        Assert.IsTrue(afterFirst >= 1, $"Expected AnchorUsage(Item) >= 1 after the first parse, got {afterFirst}");

        parser.Parse(input, "Module", out _);
        var afterSecond = parser.GrammarDiagnostics.AnchorUsage("Item");
        Assert.IsTrue(afterSecond > afterFirst,
            $"Expected accumulation across parses ({afterSecond} > {afterFirst}); GrammarDiagnostics is corpus-level");

        parser.GrammarDiagnostics.Reset();
        Assert.AreEqual(0, parser.GrammarDiagnostics.AnchorUsage("Item"), "Reset() must clear the anchor usage");
        Assert.AreEqual(0, parser.GrammarDiagnostics.TotalDistinctRecoveryPoints, "Reset() must clear the recovery points");
        Assert.AreEqual(0, parser.GrammarDiagnostics.StrictRegionFiringsAll.Count, "Reset() must clear the strict-region firings");
    }
}
