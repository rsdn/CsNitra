#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;
using System.Diagnostics;

namespace Recovery;

[TerminalMatcher]
public sealed partial class A10A11Terminals
{
    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

// A10 + A11 acceptance (end-to-end through Parse) at the DIAGNOSTIC level — per orchestrator decision.
// Tree-level resync under mask S3|S6 is blocked by known defect F8: the S6 start-rule memo patch
// short-circuits loop re-entry, so the valid tail [53..76) is absorbed by the second S6 floor and no
// structural node appears after the garbage (identical final tree with AND without the A10/A11 fix).
// The discriminators are therefore the recovery diagnostics + the S3 scan-work counter:
//   A11: the S3 stop-set must NOT contain the catch-all regex terminal "Ident" (Kind filter
//        {"Identifier","Ident"}). With the fix the S3 scan from 15 stops at the Literal ";" @51 (the
//        surviving suffix obligation); without the fix it stops at "garbage" @19 (Ident) — the Skipped
//        diagnostic at 15 is driven by TerminalKind ";" (fix) vs "Ident" (no fix).
//   A10: the token-wise jump (longest grammar terminal at non-stop positions) bounds S3 scan work —
//        parser.S3ScanPositions: with fix 19, without fix 30 (threshold 24 strictly between).
//   The first Unrecovered diagnostic marks the recovery point: 51 with the fix, 27 without.
[TestClass]
public sealed class A10A11TokenScanTests
{
    // Module := CompilationUnit; CompilationUnit := ZeroOrMany(NamespaceMember);
    // NamespaceMember := "namespace" Ident ";"  |  "class" Ident "{" "}".
    // Ident is the catch-all regex terminal (Kind "Ident") that A11 filters out of the stop-sets.
    private static Parser NewParser()
    {
        var parser = new Parser(A10A11Terminals.Trivia());
        parser.Rules["NamespaceMember"] =
        [
            new Seq([new Literal("namespace"), A10A11Terminals.Ident(), new Literal(";")], "NamespaceDeclaration"),
            new Seq([new Literal("class"), A10A11Terminals.Ident(), new Literal("{"), new Literal("}")], "ClassDeclaration"),
        ];
        parser.Rules["CompilationUnit"] = [new ZeroOrMany(new Ref("NamespaceMember"), "Members")];
        parser.Rules["Module"] = [new Ref("CompilationUnit")];

        // Determinism: S1/S2/S4/S5 excluded (S6 is the always-on floor, not mask-gated); S3 kept — the
        // token-wise scan under test. Test profile (TimeBudget = Infinite) pins DegradationLevel at 0.
        parser.Profile = RecoveryProfile.Test with { StrategyMask = RecoveryStrategy.S3 | RecoveryStrategy.S6 };
        parser.BuildTdoppRules();
        return parser;
    }

    // Valid prefix [0..15) = "namespace Pre;"; garbage [15..52) = "### garbage ### using static Foo.Bar; ";
    // resync keyword "namespace" at 53; the rest ("namespace N; class C {}") is valid to EOF (len 76).
    private const string Input = "namespace Pre; ### garbage ### using static Foo.Bar; namespace N; class C {}";

    // Full-tree walk (same shape as CSharpCorpusProbeTests.AbsorberBytes / S6BottomTests.FindAbsorbers).
    private static IEnumerable<ISyntaxNode> Walk(ISyntaxNode node)
    {
        yield return node;
        switch (node)
        {
            case SeqNode seq:
                foreach (var el in seq.RawElements)
                    foreach (var n in Walk(el))
                        yield return n;
                break;
            case ListNode list:
                foreach (var el in list.RawElements)
                    foreach (var n in Walk(el))
                        yield return n;
                foreach (var d in list.Delimiters)
                    foreach (var n in Walk(d))
                        yield return n;
                break;
            case SomeNode some:
                foreach (var n in Walk(some.Value))
                    yield return n;
                break;
        }
    }

    [TestMethod]
    public void Test_A10A11_GarbageRegion_DiagnosticLevelDiscriminators()
    {
        var parser = NewParser();
        var input = Input;
        Assert.AreEqual(76, input.Length, "test input length guard");

        var result = parser.Parse(input, "Module", out _);

        // 1. Success reaching EOF despite the garbage region.
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == 76,
            $"expected Success@76, got {result.ResultKind} (end={end})\ndiagnostics: {Describe(parser.RecoveryDiagnostics)}");

        // Diagnostics for post-hoc analysis (tree + absorbers + diagnostics + S3 scan work) — printed
        // before the asserts so a failing run still leaves the trace.
        var absorbers = Walk(node!).OfType<TerminalNode>().Where(t => t.IsAbsorber).ToList();
        Trace.WriteLine($"tree: {string.Join(" ", Walk(node!).Select(n => $"{n.GetType().Name}[{n.Kind}][{n.StartPos}..{n.EndPos})"))}");
        Trace.WriteLine($"absorbers ({absorbers.Count}): {string.Join(" ", absorbers.Select(a => $"[{a.StartPos}..{a.EndPos})"))}");
        Trace.WriteLine($"diagnostics: {Describe(parser.RecoveryDiagnostics)}");
        Trace.WriteLine($"S3ScanPositions={parser.S3ScanPositions} passes={parser.RecoveryPasses}");

        // 2. A11 discriminator: the S3 Skipped diagnostic at the garbage boundary (15) must be driven by a
        //    legitimate stop terminal — the Literal ";" (@51) or the resync keyword "namespace" (@53) — and
        //    NEVER by the catch-all regex "Ident" (without the fix the scan stops at "garbage" @19).
        //    The S3 diagnostic carries RuleName == the failing rule ("NamespaceMember"); the S6 floor's
        //    diagnostic carries RuleName == the start rule ("Module").
        var skippedAt15 = parser.RecoveryDiagnostics
            .Where(d => d.Kind == RecoveryKind.Skipped && d.StartPos == 15)
            .ToList();
        Assert.IsTrue(skippedAt15.Count > 0,
            $"expected a Skipped diagnostic at 15 (garbage boundary); got: {Describe(parser.RecoveryDiagnostics)}");
        var s3Skipped = skippedAt15.FirstOrDefault(d => d.RuleName == "NamespaceMember");
        Assert.IsNotNull(s3Skipped,
            $"expected the S3 Skipped diagnostic (rule=NamespaceMember) at 15; got: {Describe(skippedAt15)}");
        Assert.IsTrue(s3Skipped.Terminal?.Kind is ";" or "namespace",
            $"A11: the S3 stop at 15 must be driven by ';' or 'namespace', never by the catch-all 'Ident'; " +
            $"got TerminalKind={s3Skipped.Terminal?.Kind ?? "<null>"} in: {Describe(skippedAt15)}");
        Assert.IsTrue(skippedAt15.All(d => d.Terminal?.Kind != "Ident"),
            $"A11: no Skipped diagnostic at 15 may be driven by 'Ident'; got: {Describe(skippedAt15)}");

        // 3. The FIRST Unrecovered diagnostic marks the recovery point: with the fix the scan stopped deep
        //    in the garbage region (51 — the ";" before the resync keyword); without the fix it stopped at
        //    the first Ident ("garbage" @19) so the recovery point is 27.
        var firstUnrecovered = parser.RecoveryDiagnostics.FirstOrDefault(d => d.Kind == RecoveryKind.Unrecovered);
        Assert.IsNotNull(firstUnrecovered,
            $"expected an Unrecovered diagnostic (S6 floor accepted); got: {Describe(parser.RecoveryDiagnostics)}");
        Assert.IsTrue(firstUnrecovered.StartPos >= 44,
            $"expected the first Unrecovered at >= 44 (with-fix scan stop deep in the garbage: 51), got {firstUnrecovered.StartPos}");

        // 4. A10 discriminator: the token-wise jump bounds S3 scan work. MEASURED on this exact scenario:
        //    with fix = 19 scan positions (jumps over the garbage words in whole tokens), without fix = 30
        //    (char-wise scan). Threshold 24 is strictly between the two measurements.
        Assert.IsTrue(parser.S3ScanPositions <= 24,
            $"A10: S3ScanPositions must be <= 24 (measured: with fix 19, without fix 30), got {parser.S3ScanPositions}");
    }

    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) rule={d.RuleName ?? "-"} terminal={d.Terminal?.Kind ?? "-"} {d.Message}"));
}
