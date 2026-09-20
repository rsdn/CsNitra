#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// 1.6 (C2): Partial-after-recovery semantics.
//
// Two distinct final states that both reach EOF, and the contract that separates them:
//
//   * S6 (the guaranteed bottom, rank 6) is generated ONLY when parseEnd < EOF (there is a trailing
//     region to absorb). Its memo patch is always Result.Success(node, s, 0) — never Result.Partial.
//     When S6 is the accepted candidate the re-parse reads that Success memo, so the final result is
//     Success@EOF (the whole-input tree + diagnostics), NOT Partial. This is the A4-2/A5-4 contract:
//     recovery reaches EOF with a tree covering the whole input.
//
//   * Partial@EOF is a DIFFERENT, intentional state: the partial parse (via the TDOPP postfix path)
//     already reached EOF (parseEnd == EOF), so there is no trailing region and S6 is NOT generated
//     (Generate requires parseEnd < EOF). No candidate is accepted, and the raw Partial result stands.
//     It means "reached EOF, the tree covers the whole input, but the tree is incomplete (holes) and
//     recovery applied nothing". It is a recovered state (ErrorInfo == null) with empty
//     RecoveryDiagnostics — the holes are described by the tree (Partial nodes, I4), not by diagnostics.
//
// So: S6 recovery => Success@EOF (never Partial). Partial@EOF => not an S6 outcome (parseEnd == EOF).
[TerminalMatcher]
public sealed partial class PartialAfterRecoveryTerminals
{
    [Regex(@"\d+")]
    public static partial Terminal Number();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

[TestClass]
public sealed class PartialAfterRecoveryTests
{
    // ============ S6 recovery (trailing garbage) => Success@EOF, NOT Partial ============
    // Module := Expr Expr (a FIXED sequence, not a loop): after the second Expr the Seq is complete, so
    // the trailing garbage produces a clean Success below EOF with NO mismatch (snapshot == null). S5
    // returns early on snapshot == null, so S6 is the ONLY candidate (Generate emits only S6). S6's
    // absorber [parseEnd..EOF) is a Result.Success memo patch; the re-parse reads it => Success@EOF.
    // The key 1.6 assertion: the result kind is Success (not Partial).
    [TestMethod]
    public void Test_S6_Recovery_Gives_Success_Not_Partial()
    {
        var parser = new Parser(PartialAfterRecoveryTerminals.Trivia());
        parser.Rules["Expr"] = [PartialAfterRecoveryTerminals.Number()];
        parser.Rules["Module"] = [new Seq([new Ref("Expr"), new Ref("Expr")], "Module")];
        parser.BuildTdoppRules();

        var input = "12 34 56 78";
        var result = parser.Parse(input, "Module", out _);

        // S6 (the only candidate) is the accepted bottom: the result is Success, explicitly NOT Partial.
        Assert.AreEqual(Result.Kind.Success, result.ResultKind,
            $"S6 recovery must yield Success, got {result.ResultKind}");
        Assert.IsTrue(result.TryGetSuccess(out _, out var end), "S6 recovery must yield a Success result");
        Assert.AreEqual(input.Length, end, "S6 bottom must reach EOF");
        Assert.IsNull(parser.ErrorInfo, "S6 recovery to EOF must clear ErrorInfo");
        // S6 emits a Skipped ("bottom skip") diagnostic — evidence the bottom absorber was applied.
        Assert.IsTrue(parser.RecoveryDiagnostics.Any(d => d.Kind == RecoveryKind.Skipped),
            "expected an S6 bottom-skip (Skipped) diagnostic");
    }

    // ============ Partial@EOF is a distinct state: reached EOF, not an S6 outcome ============
    // TDOPP postfix path: Expr = "a" | "a+" | "b" | Seq(Ref(Expr), "+", ReqRef(Expr,100), Inner Seq("b","c")).
    // Input "a+bb": the postfix Seq parses "+ b" then Inner Seq("b","c") is missing "c" at EOF => base-case
    // Partial in ParseSeq, propagated up the postfix path => Partial@4 = Partial@EOF. Because parseEnd == EOF
    // there is no trailing region, so S6 is NOT generated (Generate requires parseEnd < EOF) and no candidate
    // is accepted. The raw Partial@EOF stands: ErrorInfo == null (recovered state) and RecoveryDiagnostics is
    // empty. This is the intentional meaning of Partial@EOF — NOT a S6 recovery outcome.
    [TestMethod]
    public void Test_PartialAtEof_Is_Distinct_State_Not_S6()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));
        parser.Rules["Expr"] = new Rule[]
        {
            new Literal("a"),
            new Literal("a+"),
            new Literal("b"),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100), new Seq([new Literal("b"), new Literal("c")], "Inner")], "Add"),
        };
        parser.BuildTdoppRules();

        var input = "a+bb";
        var result = parser.Parse(input, "Expr", out _);

        // Partial@EOF: the kind is Partial (explicitly NOT Success), the tree reaches EOF, and it is a
        // recovered state (ErrorInfo == null) with no accepted candidate (empty diagnostics).
        Assert.AreEqual(Result.Kind.Partial, result.ResultKind,
            $"expected Partial@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length}");
        Assert.IsTrue(result.TryGetPartial(out _, out var end), "expected a Partial result");
        Assert.AreEqual(input.Length, end, "Partial@EOF: the partial tree must reach EOF");
        Assert.IsNull(parser.ErrorInfo, "Partial@EOF is a recovered state — ErrorInfo must be null");
        Assert.AreEqual(0, parser.RecoveryDiagnostics.Count,
            "Partial@EOF arises without an accepted candidate (S6 not generated: parseEnd == EOF)");
    }
}
