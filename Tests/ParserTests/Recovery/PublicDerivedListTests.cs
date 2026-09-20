#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// A5-2 (6.1.2c): the public RecoveryDiagnostics list is DERIVED from the final tree:
//   DeriveRecoveryDiagnostics(finalTree)  (the node-attached diagnostics — the exact accumulated
//                                          instances — in tree position order)
//   + the Unrecovered marker(s) from the cache (Unrecovered is not a tree node — A4-2 — so it is
//     appended, not tree-derived).
// These tests pin that contract: public == derived (exact instances, in order) + Unrecovered, for a
// case WITHOUT the S6 floor (no Unrecovered) and a case WITH it (Unrecovered present, appended last).
[TestClass]
public sealed class PublicDerivedListTests
{
    // Module := a b c — a missing "b" is an S1 zero-width insertion; no S6 → no Unrecovered.
    private static Parser NewInsertionParser()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());
        parser.Rules["Module"] = [new Seq([new Literal("a"), new Literal("b"), new Literal("c")], "Module")];
        parser.BuildTdoppRules();
        return parser;
    }

    // Module := Expr Expr, Expr := Number — trailing garbage is absorbed by the S6 floor → Unrecovered.
    private static Parser NewAbsorberParser()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());
        parser.Rules["Expr"] = [RecoveryTerminals.Number()];
        parser.Rules["Module"] = [new Seq([new Ref("Expr"), new Ref("Expr")], "Module")];
        parser.BuildTdoppRules();
        return parser;
    }

    // ============ 1. No S6: the public list IS the derived list (same instances, order, count) ============
    [TestMethod]
    public void Test_PublicList_EqualsDerived_NoUnrecovered()
    {
        var parser = NewInsertionParser();
        var input = "a c";
        var result = parser.Parse(input, "Module", out _);
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos}");

        var derived = parser.DeriveRecoveryDiagnostics(node).ToList();
        var pub = parser.RecoveryDiagnostics.ToList();

        // No Unrecovered anywhere: S6 was not the accepted fallback (a pure S1 insertion).
        Assert.IsFalse(pub.Any(d => d.Kind == RecoveryKind.Unrecovered), Describe(pub));
        Assert.IsFalse(derived.Any(d => d.Kind == RecoveryKind.Unrecovered), Describe(derived));
        Assert.IsTrue(derived.Any(d => d.Kind == RecoveryKind.Inserted),
            $"precondition: the derived list has the S1 insertion, got: {Describe(derived)}");

        // The public list is EXACTLY the derived list: same count, same instances, same order.
        Assert.AreEqual(derived.Count, pub.Count, $"derived={Describe(derived)} public={Describe(pub)}");
        for (var i = 0; i < derived.Count; i++)
            Assert.IsTrue(ReferenceEquals(derived[i], pub[i]),
                $"element {i} is not the exact derived instance: derived={Describe(derived)} public={Describe(pub)}");
    }

    // ============ 2. S6 floor: public list == derived list + the Unrecovered marker (appended) ============
    [TestMethod]
    public void Test_PublicList_EqualsDerivedPlusUnrecovered_S6Fallback()
    {
        var parser = NewAbsorberParser();
        var input = "12 34 ###";
        var result = parser.Parse(input, "Module", out _);
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos}");

        var derived = parser.DeriveRecoveryDiagnostics(node).ToList();
        var pub = parser.RecoveryDiagnostics.ToList();

        // Precondition: the S6 floor was the accepted fallback → exactly one Unrecovered marker.
        var unrecovered = pub.Where(d => d.Kind == RecoveryKind.Unrecovered).ToList();
        Assert.AreEqual(1, unrecovered.Count, $"expected exactly 1 Unrecovered (S6 floor), got: {Describe(pub)}");

        // The derived (tree) part carries no Unrecovered — it is result-derived (A4-2), not a tree node.
        Assert.IsFalse(derived.Any(d => d.Kind == RecoveryKind.Unrecovered), Describe(derived));
        Assert.IsTrue(derived.Any(d => d.Kind == RecoveryKind.Skipped),
            $"precondition: the derived list has the S6 absorber, got: {Describe(derived)}");

        // public == derived (the first derived.Count entries, exact instances, in order) ...
        Assert.AreEqual(derived.Count + 1, pub.Count, $"derived={Describe(derived)} public={Describe(pub)}");
        for (var i = 0; i < derived.Count; i++)
            Assert.IsTrue(ReferenceEquals(derived[i], pub[i]),
                $"element {i} is not the exact derived instance: derived={Describe(derived)} public={Describe(pub)}");

        // ... + the Unrecovered marker appended LAST, at the same recovery point as the S6 absorber,
        // zero-width.
        Assert.IsTrue(ReferenceEquals(unrecovered[0], pub[^1]),
            $"the Unrecovered must be the last (appended) entry: derived={Describe(derived)} public={Describe(pub)}");
        Assert.IsTrue(unrecovered[0].StartPos == unrecovered[0].EndPos,
            $"Unrecovered must be zero-width, got [{unrecovered[0].StartPos}..{unrecovered[0].EndPos})");
        Assert.AreEqual(derived[0].StartPos, unrecovered[0].StartPos,
            $"Unrecovered must sit at the S6 absorber's recovery point: derived={Describe(derived)} public={Describe(pub)}");
    }

    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) term={d.Terminal?.Kind ?? "-"} {d.Message}"));
}
