#nullable enable

using System.Text;
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

    // ============ 6.3.2: grammar for the corpus-level findings (declared anchors + a strict region) ============

    // A grammar with the recovery annotations needed to exercise every negative finding over a corpus:
    //   - Item    : the USED anchor (the S2 resync target) — the corpus inputs are "int aN;" items.
    //   - AltItem : a DECLARED anchor that is NEVER used (the corpus inputs contain no "alt" token) →
    //               the "anchor never used" finding.
    //   - Strict  : a DECLARED strict region (Recoverable=false) that is reachable in the grammar (Stmt)
    //               but NEVER fires on the corpus (no "strict" token, so no failure inside it) → the
    //               "strict region never fires" finding.
    //   - Module  : the loop, annotated with Anchors = [Item, AltItem] (the declared anchor set).
    private static Parser NewQualityCorpusParser()
    {
        var parser = new Parser(GrammarDiagTerminals.Trivia());
        parser.MaxRecoveryAttemptsPerPosition = 16;
        parser.Rules["Item"] =
        [
            new Seq([new Literal("int"), GrammarDiagTerminals.Ident(), new Literal(";")], "Item"),
        ];
        parser.Rules["AltItem"] =
        [
            new Seq([new Literal("alt"), GrammarDiagTerminals.Ident(), new Literal(";")], "AltItem"),
        ];
        parser.Rules["Strict"] =
        [
            new RecoveryRule(new Seq([new Literal("strict"), GrammarDiagTerminals.Ident(), new Literal(";")], "Strict"),
                             new RecoveryOptions { Recoverable = false }),
        ];
        parser.Rules["Stmt"] =
        [
            new Ref("Item"),
            new Ref("AltItem"),
            new Ref("Strict"),
        ];
        parser.Rules["Module"] =
        [
            new RecoveryRule(new ZeroOrMany(new Ref("Stmt"), "Stmts"), new RecoveryOptions { Anchors = [new Ref("Item"), new Ref("AltItem")] }),
        ];
        parser.BuildTdoppRules();
        return parser;
    }

    // A representative slice of the D1 corpus (the many-small-errors scenario, like D1.1/D1.6): N well-
    // formed "int aN;" items separated by "###" garbage. Each "###" forces one S2 resync to the next
    // Item, so over a corpus of these the Item anchor is used once per garbage token (accumulating).
    private static string GenManyItemErrors(int n)
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= n; i++)
        {
            sb.Append($"int a{i};");
            if (i < n)
                sb.Append(" ### ");
        }
        return sb.ToString();
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

    // ============ 6.3.2: GetFindings — the quality analysis (controlled, no parser) ============

    // Populate GrammarDiagnostics directly via the Note* hooks and assert GetFindings produces the right
    // kinds. Item is used 15x (heavily used, > default threshold 10) and is the ONLY used anchor (so the
    // S2 anchor set is redundant); AltItem is declared but never used; Strict is declared but never fires.
    [TestMethod]
    public void Test_GetFindings_NegativeAndRedundancyKinds()
    {
        var gd = new GrammarDiagnostics();
        for (var i = 0; i < 15; i++)
            gd.NoteAnchorUse("Item");
        // Note: no NoteAnchorUse("AltItem") — declared but never used. No NoteStrictRegionFiring("Strict").

        var findings = gd.GetFindings(declaredAnchors: ["Item", "AltItem"], declaredStrictRegions: ["Strict"]);
        var text = string.Join('\n', findings);

        // (1) The declared-but-never-used anchor (AltItem) is reported; the used anchor (Item) is not.
        Assert.IsTrue(findings.Any(f => f.Kind == GrammarFindingKind.AnchorNeverUsed && f.Name == "AltItem"),
            $"Expected AnchorNeverUsed for AltItem:\n{text}");
        Assert.IsFalse(findings.Any(f => f.Kind == GrammarFindingKind.AnchorNeverUsed && f.Name == "Item"),
            "Item is used → it must not be reported as never-used");

        // (2) The heavily used anchor (Item, 15 > 10) is reported with its count.
        Assert.IsTrue(findings.Any(f => f.Kind == GrammarFindingKind.AnchorHeavilyUsed && f.Name == "Item" && f.Count == 15),
            $"Expected AnchorHeavilyUsed for Item (15 uses):\n{text}");

        // (4) The declared-but-never-firing strict region (Strict) is reported.
        Assert.IsTrue(findings.Any(f => f.Kind == GrammarFindingKind.StrictRegionNeverFires && f.Name == "Strict"),
            $"Expected StrictRegionNeverFires for Strict:\n{text}");

        // (5) Only one distinct S2 anchor (Item) is ever used → the anchor set is redundant.
        Assert.IsTrue(findings.Any(f => f.Kind == GrammarFindingKind.S2AnchorRedundant && f.Name == "Item"),
            $"Expected S2AnchorRedundant (only Item used):\n{text}");

        // (3) No recovery points were recorded → no RuleRecoveryPointsIdentical finding.
        Assert.IsFalse(findings.Any(f => f.Kind == GrammarFindingKind.RuleRecoveryPointsIdentical),
            $"No recovery points recorded → no identical-points finding expected:\n{text}");
    }

    // Signal 2: a recovered rule with exactly one distinct recovery point is reported; a rule with two
    // distinct points is not.
    [TestMethod]
    public void Test_GetFindings_RecoveryPointsIdentical()
    {
        var gd = new GrammarDiagnostics();
        gd.NoteRecoveryPoint("Item", new SeqFrameLocation(2));
        gd.NoteRecoveryPoint("Item", new SeqFrameLocation(2)); // same point again → still 1 distinct
        gd.NoteRecoveryPoint("Other", new SeqFrameLocation(0));
        gd.NoteRecoveryPoint("Other", new SeqFrameLocation(3)); // two distinct points

        var findings = gd.GetFindings(declaredAnchors: [], declaredStrictRegions: []);
        var text = string.Join('\n', findings);

        Assert.IsTrue(findings.Any(f => f.Kind == GrammarFindingKind.RuleRecoveryPointsIdentical && f.Name == "Item"),
            $"Expected RuleRecoveryPointsIdentical for Item (1 distinct point):\n{text}");
        Assert.IsFalse(findings.Any(f => f.Kind == GrammarFindingKind.RuleRecoveryPointsIdentical && f.Name == "Other"),
            "Other has 2 distinct points → it must not be reported as identical:\n{text}");

        // With two used anchors, the S2 set is NOT redundant (guards the Count == 1 gate).
        gd.NoteAnchorUse("Item");
        gd.NoteAnchorUse("Other");
        var findings2 = gd.GetFindings(declaredAnchors: [], declaredStrictRegions: []);
        Assert.IsFalse(findings2.Any(f => f.Kind == GrammarFindingKind.S2AnchorRedundant),
            "Two distinct anchors used → the S2 anchor set is not redundant");
    }

    // ============ 6.3.2: corpus test — negative signals over a representative D1 slice ============

    // Run a representative slice of the D1 corpus (the many-small-errors scenario) against the quality
    // grammar and assert the findings include the expected negative signals: the deliberately-declared-
    // but-never-used anchor (AltItem) and the strict region that never fires (Strict) are both reported,
    // and the used anchor (Item) is heavily used and the only S2 anchor (redundant set).
    [TestMethod]
    public void Test_Findings_CorpusSlice_NegativeSignals()
    {
        var parser = NewQualityCorpusParser();
        // A corpus = several dirty inputs parsed against the SAME parser (GrammarDiagnostics accumulates).
        parser.Parse(GenManyItemErrors(12), "Module", out _);
        parser.Parse(GenManyItemErrors(12), "Module", out _);

        var findings = parser.GrammarDiagnostics.GetFindings(parser);
        var text = string.Join('\n', findings);

        // Sanity: the used anchor (Item) actually accumulated usage over the corpus.
        Assert.IsTrue(parser.GrammarDiagnostics.AnchorUsage("Item") > 0,
            "Expected the Item anchor to be used by the S2 resyncs over the corpus");

        // (1) The deliberately-declared-but-never-used anchor (AltItem) is reported.
        Assert.IsTrue(findings.Any(f => f.Kind == GrammarFindingKind.AnchorNeverUsed && f.Name == "AltItem"),
            $"Expected AnchorNeverUsed for the never-used anchor AltItem:\n{text}");
        Assert.IsFalse(findings.Any(f => f.Kind == GrammarFindingKind.AnchorNeverUsed && f.Name == "Item"),
            "Item is used over the corpus → it must not be reported as never-used");

        // (4) The declared strict region (Strict) that never fires on the corpus is reported.
        Assert.IsTrue(findings.Any(f => f.Kind == GrammarFindingKind.StrictRegionNeverFires && f.Name == "Strict"),
            $"Expected StrictRegionNeverFires for the never-firing strict region Strict:\n{text}");

        // (2) The used anchor (Item) is heavily used above the default threshold over the corpus.
        Assert.IsTrue(findings.Any(f => f.Kind == GrammarFindingKind.AnchorHeavilyUsed && f.Name == "Item"),
            $"Expected AnchorHeavilyUsed for Item (used on every resync):\n{text}");

        // (5) Only one distinct S2 anchor (Item) is ever used → the declared anchor set is redundant.
        Assert.IsTrue(findings.Any(f => f.Kind == GrammarFindingKind.S2AnchorRedundant && f.Name == "Item"),
            $"Expected S2AnchorRedundant (only Item is ever used):\n{text}");
    }
}
