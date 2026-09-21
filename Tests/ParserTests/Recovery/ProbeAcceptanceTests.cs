#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// 5b.4.3 (A5-1 D2/D3): E2E — DERIVED-probe acceptance in S2 (GenerateS2), no author anchors.
//
// Both grammars declare NO author Anchors and NO author CanStart, so every S2 resync candidate
// must come from DeriveProbePredicates (derived, Derived=true). The derived predicate is probed
// by the bounded `Probe` (dedicated probeScratch, depth ceiling ProbeDepthCeiling = 8) instead of
// the full `Speculative` parse, and is accepted by the word-count rule (D2):
//
//     endPos > s && CountWords(input, Trivia, s, endPos) >= softDepth      (softDepth default 2)
//
// where endPos = full-success end, or the ceiling-cut position (D3 work ceiling), or -1 (plain
// failure). Author/loop anchors keep the strict semantics (T1: ok && endPos > s; T2: ok).
//
// Test A (ceiling cut, >= K words): the C1..C10 Ref chain consumes no input, so the probe of
// "Item" at 8 is CUT by the 8-frame ceiling before reaching C10's failing "z"; the consumed span
// [8..12) holds 2 words ("v w") >= SoftDepth(2) → T1 resync at 8 (pre-5b.4.3: plain failure → no
// T1 → S3 skip-to-EOF, no "resync point" diagnostic).
//
// Test B (full success, < K words): the probe of "Body" at 4 FULLY SUCCEEDS on "q" but the span
// [4..5) holds 1 word < SoftDepth(2) → D2 REJECTS it (pre-5b.4.3 strict `ok` → accepted →
// "resync point 4" diagnostic) → no S2 at all → S3 skip-to-terminator recovers to EOF.
[TerminalMatcher]
public sealed partial class ProbeAcceptanceTerminals
{
    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

[TestClass]
public sealed class ProbeAcceptanceTests
{
    private static string Describe(Parser parser)
        => string.Join("; ", parser.RecoveryDiagnostics.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) «{d.Message}» rule={d.RuleName ?? "-"}"));

    // Module = ZeroOrMany(Item, "Items"); Item = "v" Deep; Deep = "w" C1; C1 → C2 → ... → C9 → C10;
    // C10 = "z". The 10-level Ref chain descends 11 frames (Item, Deep, C1..C10) — deeper than the
    // ProbeDepthCeiling = 8 — so the probe is ceiling-cut in the middle of the C chain.
    private static Parser MakeDeepProbeParser()
    {
        var parser = new Parser(ProbeAcceptanceTerminals.Trivia());
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Item"), "Items")];
        parser.Rules["Item"] = [new Seq([new Literal("v"), new Ref("Deep")], "ItemSeq")];
        parser.Rules["Deep"] = [new Seq([new Literal("w"), new Ref("C1")], "DeepSeq")];
        parser.Rules["C1"] = [new Ref("C2")];
        parser.Rules["C2"] = [new Ref("C3")];
        parser.Rules["C3"] = [new Ref("C4")];
        parser.Rules["C4"] = [new Ref("C5")];
        parser.Rules["C5"] = [new Ref("C6")];
        parser.Rules["C6"] = [new Ref("C7")];
        parser.Rules["C7"] = [new Ref("C8")];
        parser.Rules["C8"] = [new Ref("C9")];
        parser.Rules["C9"] = [new Ref("C10")];
        parser.Rules["C10"] = [new Literal("z")];
        parser.BuildTdoppRules();
        return parser;
    }

    // Module = ZeroOrMany(Body, "Bodies"); Body = "v" Tok | "q"; Tok = "t". The probe of "Body"
    // at 4 fully succeeds on the "q" alternative — a single word, below SoftDepth(2).
    private static Parser MakeShallowProbeParser()
    {
        var parser = new Parser(ProbeAcceptanceTerminals.Trivia());
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Body"), "Bodies")];
        parser.Rules["Body"] =
        [
            new Seq([new Literal("v"), new Ref("Tok")], "BodySeq"),
            new Literal("q"),
        ];
        parser.Rules["Tok"] = [new Literal("t")];
        parser.BuildTdoppRules();
        return parser;
    }

    // D3 (ceiling cut) + D2 (word count): the probe of the derived loop-body anchor "Item" at 8 is
    // cut by the 8-frame ceiling (the C1..C10 chain consumes no input) after consuming "v w" =
    // 2 words >= SoftDepth(2) → T1 resync at 8; the second recovery round (S3 skip-to-terminator)
    // absorbs the trailing "#" → Success@EOF with exactly one "resync point" diagnostic.
    [TestMethod]
    public void Test_Probe_CeilingCut_GeKWords_Accepted()
    {
        var parser = MakeDeepProbeParser();
        var input = "v w ### v w #";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == input.Length,
            $"Expected Success@EOF via derived-probe T1 resync, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} diag={Describe(parser)}");
        Assert.IsNull(parser.ErrorInfo,
            $"Expected ErrorInfo null (recovered), got ErrorInfo@{parser.ErrorInfo?.Pos}. diag={Describe(parser)}");

        // The S2 resync diagnostic is present and points at 8 (the "v" of the second "v w" item).
        var resync = parser.RecoveryDiagnostics.Single(d => d.Kind == RecoveryKind.Skipped && d.Message.Contains("resync point"));
        Assert.IsTrue(resync.Message.Contains("resync point 8"), $"unexpected resync message: {resync.Message}");

        // Driving anchor = "Item" — the derived (non-author) loop-body Ref of Module.
        Assert.AreEqual(1, parser.GrammarDiagnostics.AnchorUsage("Item"),
            $"the derived loop-body anchor must drive the T1 resync; used=[{string.Join(",", parser.GrammarDiagnostics.UsedAnchors)}] diags={Describe(parser)}");
    }

    // D2 (word-count rejection): the probe of the derived loop-body anchor "Body" at 4 FULLY
    // SUCCEEDS ("q") but the consumed span holds 1 word < SoftDepth(2) → rejected → no S2 resync
    // at all → S3 skip-to-terminator recovers to EOF with zero "resync point" diagnostics.
    [TestMethod]
    public void Test_Probe_FullSuccess_LtKWords_Rejected()
    {
        var parser = MakeShallowProbeParser();
        var input = "v # q";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == input.Length,
            $"Expected Success@EOF via S3 terminator skip, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} diag={Describe(parser)}");
        Assert.IsNull(parser.ErrorInfo,
            $"Expected ErrorInfo null (recovered), got ErrorInfo@{parser.ErrorInfo?.Pos}. diag={Describe(parser)}");
        Assert.AreEqual(0, parser.RecoveryDiagnostics.Count(d => d.Message.Contains("resync point")),
            $"the derived probe must be REJECTED by the word-count acceptance (< SoftDepth words); diags={Describe(parser)}");
        Assert.AreEqual(0, parser.GrammarDiagnostics.AnchorUsage("Body"),
            $"the derived anchor must not drive any S2 resync; used=[{string.Join(",", parser.GrammarDiagnostics.UsedAnchors)}] diags={Describe(parser)}");
    }
}
