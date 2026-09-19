#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
namespace Recovery;

[TerminalMatcher]
public sealed partial class A42Terminals
{
    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

// A4-2 / C1 acceptance (sub-point 1.3.6): end-to-end parses that prove the two reporting contracts
// introduced by 1.3.4 (Unrecovered emission) and 1.5/C1 (strict ⇒ only S6).
//
// D1.2 — a file with 3+ errors where one is "unsavable" (no S0–S5 repair gives progress, so S6 is the
//       fallback for that point): the parse must still reach Success@EOF, report the unsavable error as
//       Unrecovered, AND report the recoverable errors (Inserted/Skipped).
// D1.3 — a strict region (Recoverable:false) that contains an error: the parse reaches Success@EOF via
//       the S6 bottom, emits Unrecovered, and emits NO repair-candidate (S1–S5) diagnostics from inside
//       the region (C1: strict ⇒ only S6).
//
// Both tests are GENERAL (no budget tuning): they run with the default MaxRecoveryAttemptsPerPosition (3).
[TestClass]
public sealed class A42AcceptanceTests
{
    // ============ D1.2 grammar ============
    // Module := Seq(Statement, Statement, Statement) — a FIXED sequence (not a loop): after the last
    // statement the Seq is complete, so trailing garbage is a clean Success below EOF with NO mismatch
    // (snapshot == null) — the exact scenario where Generate emits only S6 (S1–S4 need a snapshot, S5
    // returns early on snapshot == null).
    // Statement := Seq(int, Ident, ";") — a plain (non-nullable) ";" so a missing ";" is a genuine
    // terminal mismatch (a Failure, not a silently-absorbed Partial): the recovery engine repairs it with
    // an S1 insertion and emits an Inserted diagnostic (a recoverable error).
    private static Parser NewD12Parser()
    {
        var parser = new Parser(A42Terminals.Trivia());
        parser.Rules["Statement"] = [new Seq([new Literal("int"), A42Terminals.Ident(), new Literal(";")], "Statement")];
        parser.Rules["Module"] = [new Seq([new Ref("Statement"), new Ref("Statement"), new Ref("Statement")], "Module")];
        parser.BuildTdoppRules();
        return parser;
    }

    // D1.2 input: 3 errors.
    //   "int a"  — missing ";" (recoverable, S1 insertion)
    //   "int b"  — missing ";" (recoverable, S1 insertion)
    //   "int c ;"— correct
    //   "$"      — trailing garbage matching no expected terminal (unsavable, S6 fallback)
    private const string D12Input = "int a int b int c ; $";

    [TestMethod]
    public void Test_D1_2_MixedErrors_UnsavableReportedAsUnrecovered()
    {
        var parser = NewD12Parser();
        var input = D12Input;
        var result = parser.Parse(input, "Module", out _);

        // 1. The parse reaches Success@EOF despite the unsavable error.
        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == input.Length,
            $"D1.2 expected Success@EOF, got {result.ResultKind} (end={end}, len={input.Length})\n{Describe(parser.RecoveryDiagnostics)}");
        Assert.IsNull(parser.ErrorInfo, "D1.2: recovered to EOF must clear ErrorInfo");

        // 2. The unsavable error is reported as Unrecovered (S6 was the accepted fallback for that point).
        var unrecovered = parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Unrecovered).ToList();
        Assert.IsTrue(unrecovered.Count >= 1,
            $"D1.2 expected >= 1 Unrecovered (the unsavable error), got: {Describe(parser.RecoveryDiagnostics)}");

        // 3. The recoverable errors are ALSO reported (Inserted/Skipped diagnostics are present).
        var repair = parser.RecoveryDiagnostics.Where(d => d.Kind is RecoveryKind.Inserted or RecoveryKind.Skipped).ToList();
        Assert.IsTrue(repair.Count >= 2,
            $"D1.2 expected >= 2 repair (Inserted/Skipped) diagnostics for the recoverable errors, got: {Describe(parser.RecoveryDiagnostics)}");

        // 4. The Unrecovered point is the boundary of the valid prefix (the trailing garbage region),
        //    strictly AFTER the last recoverable (Inserted) error and before EOF — NOT inside the
        //    recoverable errors themselves.
        var lastInsertedPos = parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Inserted).Max(d => d.StartPos);
        Assert.IsTrue(unrecovered.Any(d => d.StartPos > lastInsertedPos && d.StartPos < input.Length),
            $"D1.2 expected Unrecovered at the valid-prefix boundary (after pos {lastInsertedPos}, before EOF), got: {Describe(parser.RecoveryDiagnostics)}");
    }

    // ============ D1.3 grammar ============
    // Module := Seq(Literal("a"), StrictExpr); StrictExpr := RecoveryRule(Literal("b"), Recoverable:false).
    // The strict region (Recoverable:false) is the same construction as S6BottomTests.
    // Test_S6_Only_Generated_In_Strict_Region, but exercised end-to-end through Parse (not a direct
    // RecoveryEngine.Generate call).
    private static Parser NewD13Parser()
    {
        var parser = new Parser(A42Terminals.Trivia());
        var strictExpr = new RecoveryRule(new Literal("b"), new RecoveryOptions { Recoverable = false });
        parser.Rules["StrictExpr"] = [strictExpr];
        parser.Rules["Module"] = [new Seq([new Literal("a"), new Ref("StrictExpr")], "Module")];
        parser.BuildTdoppRules();
        return parser;
    }

    // D1.3 input: "a c" — the strict region expects "b" but the input has "c" (the error is inside the
    // strict region).
    private const string D13Input = "a c";

    [TestMethod]
    public void Test_D1_3_StrictRegion_SuccessAtEof_Unrecovered_NoRepair()
    {
        var parser = NewD13Parser();
        var input = D13Input;
        var result = parser.Parse(input, "Module", out _);

        // 1. The parse reaches Success@EOF (S6 is the only candidate in the strict region, per C1).
        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == input.Length,
            $"D1.3 expected Success@EOF, got {result.ResultKind} (end={end}, len={input.Length})\n{Describe(parser.RecoveryDiagnostics)}");
        Assert.IsNull(parser.ErrorInfo, "D1.3: recovered to EOF must clear ErrorInfo");

        // 2. S6 was the accepted fallback, so an Unrecovered diagnostic is emitted at the recovery point.
        var unrecovered = parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Unrecovered).ToList();
        Assert.IsTrue(unrecovered.Count >= 1,
            $"D1.3 expected >= 1 Unrecovered (S6 was the accepted fallback), got: {Describe(parser.RecoveryDiagnostics)}");

        // 3. NO repair-candidate diagnostics from inside the strict region: S1–S5 are suppressed (C1), so
        //    there are zero Inserted diagnostics (the only kind S1/S4 produce).
        var inserted = parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Inserted).ToList();
        Assert.AreEqual(0, inserted.Count,
            $"D1.3 strict region must emit no Inserted (S1/S4) diagnostics, got: {Describe(parser.RecoveryDiagnostics)}");

        // 4. Any Skipped diagnostic present is the S6 "bottom skip" (the only candidate in the strict
        //    region), never an S2/S3/S5 resync/skip. In the strict region S6 is the sole candidate, so a
        //    Skipped diagnostic, if any, must pair 1:1 with the Unrecovered emission.
        var skipped = parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Skipped).ToList();
        Assert.IsTrue(skipped.Count <= unrecovered.Count,
            $"D1.3 expected at most one Skipped (the S6 bottom) per Unrecovered, got: {Describe(parser.RecoveryDiagnostics)}");
    }

    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) rule={d.RuleName ?? "-"} {d.Message}"));
}
#endif
