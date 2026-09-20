#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// A5-2 (6.1.2b2, D2): soft separator (5b.3.2, A5-5) — consumed node is a REGULAR terminal
// (NOT IsRecovery), so the 6.1.2b session-end position+shape match does NOT cover it. The Skipped
// diagnostic is attached to the consumed node directly in ParseSeparatedList, at the point it is
// emitted (same guards as the list Add) — DeriveRecoveryDiagnostics(root) returns it at the right
// position. Previously this input yielded nothing for the soft separator (D2 open).
[TestClass]
public sealed class SideTableSoftSepTests
{
    // Item = Number; List = SeparatedList(Item, ",", SoftSeparator: ";") — grammar from SoftSeparatorTests.
    private static Parser NewParser()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));
        parser.Rules["Item"] = [RecoveryTerminals.Number()];
        parser.Rules["List"] =
        [
            new SeparatedList(new Ref("Item"), new Literal(","), Kind: "List", SoftSeparator: new Literal(";"))
        ];
        parser.BuildTdoppRules();
        return parser;
    }

    [TestMethod]
    public void Test_D2_SoftSeparator_SkippedDiagnosticDerivedFromNode()
    {
        var parser = NewParser();
        var input = "1;2";
        var result = parser.Parse(input, "List", out _);
        if (!result.TryGetSuccess(out var root, out var end) || end != input.Length || root is null)
            Assert.Fail($"expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos}");
        Assert.IsNull(parser.ErrorInfo);

        // The accumulated list holds exactly one Skipped soft-separator diagnostic (5b.3.2).
        var accumulated = parser.RecoveryDiagnostics;
        Assert.AreEqual(1, accumulated.Count, $"expected exactly the soft-separator diagnostic, got: {Describe(accumulated)}");
        var acc = accumulated[0];
        Assert.AreEqual(RecoveryKind.Skipped, acc.Kind, Describe(accumulated));
        Assert.AreEqual(1, acc.StartPos, Describe(accumulated));
        Assert.AreEqual(2, acc.EndPos, Describe(accumulated));

        // D2: derive returns the soft-separator diagnostic attached at its emission point (the consumed
        // node is a regular terminal in ListNode.Delimiters — the session-end match cannot see it).
        var derived = parser.DeriveRecoveryDiagnostics(root);
        Assert.AreEqual(1, derived.Count, $"expected exactly the soft-separator diagnostic derived, got: {Describe(derived)}");
        var d = derived[0];
        Assert.IsTrue(ReferenceEquals(acc, d), $"derived must be the exact accumulated instance, got: {Describe(derived)}");
        Assert.AreEqual(RecoveryKind.Skipped, d.Kind, Describe(derived));
        Assert.AreEqual(1, d.StartPos, Describe(derived));
        Assert.AreEqual(2, d.EndPos, Describe(derived));
        Assert.IsNotNull(d.Terminal, $"D4: Terminal must be non-null, got: {Describe(derived)}");
        Assert.AreEqual(";", d.Terminal!.Kind, Describe(derived));
        Assert.AreEqual("List", d.RuleName, Describe(derived));
    }

    [TestMethod]
    public void Test_D2_SoftSeparator_Multiple_AllDerived()
    {
        var parser = NewParser();
        var input = "1;2;3";
        var result = parser.Parse(input, "List", out _);
        if (!result.TryGetSuccess(out var root, out var end) || end != input.Length || root is null)
            Assert.Fail($"expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos}");
        Assert.IsNull(parser.ErrorInfo);

        var accumulated = parser.RecoveryDiagnostics;
        Assert.AreEqual(2, accumulated.Count, $"expected the two soft-separator diagnostics, got: {Describe(accumulated)}");

        var derived = parser.DeriveRecoveryDiagnostics(root);
        Assert.AreEqual(2, derived.Count, $"expected both soft-separator diagnostics derived, got: {Describe(derived)}");
        for (var i = 0; i < 2; i++)
        {
            Assert.IsTrue(ReferenceEquals(accumulated[i], derived[i]),
                $"derived[{i}] must be the exact accumulated instance, got: {Describe(derived)}");
            Assert.AreEqual(RecoveryKind.Skipped, derived[i].Kind, Describe(derived));
            Assert.AreEqual(";", derived[i].Terminal!.Kind, Describe(derived));
            Assert.AreEqual("List", derived[i].RuleName, Describe(derived));
        }
        CollectionAssert.AreEqual(new int[] { 1, 3 }, derived.Select(d => d.StartPos).ToArray());
    }

    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) \"{d.Message}\" term={d.Terminal?.Kind} rule={d.RuleName}"));
}
