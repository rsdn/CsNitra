#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// A5-2 (6.1.2a): node-attached recovery diagnostics via a side-table (reference identity).
// Parser.AttachDiagnostic(node, diag) attaches a full-metadata RecoveryDiagnostic to a node;
// Parser.DeriveRecoveryDiagnostics(root) walks the final tree, collects each node's attached
// diagnostics by reference identity, and returns them sorted by (StartPos, EndPos).
// This is the reference-identity lookup — the pure structure-based
// DiagnosticDerivation.DeriveDiagnostics (6.1.1) is untouched and still present.
// The tree here is hand-built (no full Parse needed): the side-table is initialized in the
// Parser constructor (and fresh per Parse), so a freshly-constructed Parser is usable directly.
[TestClass]
public sealed class SideTableTests
{
    // ============ 1. Derive returns exactly the attached diagnostics, sorted by (StartPos, EndPos), metadata intact ============
    // Hand-built tree covering every node kind the walk descends into:
    //   root (SeqNode) -> a (TerminalNode)
    //                    -> list (ListNode) -> b1 (element), d1 (delimiter)
    //                    -> opt (SomeNode)  -> c1 (value)
    // One diagnostic is attached to each of a/b1/d1/c1/root (5 nodes), in a scrambled (unsorted)
    // order. The nodes without an attachment (list, opt) must contribute nothing. Derive must
    // return exactly the 5, ordered by (StartPos, EndPos), with every metadata field
    // (Kind, Terminal, RuleName, Message) intact.
    [TestMethod]
    public void Test_SideTable_Derive_ReturnsAttachedSortedByPosition()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());

        var a = new TerminalNode("A", 0, 2, 2);
        var b1 = new TerminalNode("B1", 3, 4, 1);
        var d1 = new TerminalNode(",", 4, 5, 1);
        var list = new ListNode("List", [b1], [d1], 3, 6);
        var c1 = new TerminalNode("C", 6, 8, 2);
        var opt = new SomeNode("Opt", c1, 6, 8);
        var root = new SeqNode("Module", [a, list, opt], 0, 10);

        var tNum = RecoveryTerminals.Number();
        var tIdent = RecoveryTerminals.Ident();
        var tOp = RecoveryTerminals.ErrorOperator();

        // Scrambled (unsorted) attachment order across all node kinds.
        var diagRoot = new RecoveryDiagnostic(8, 9, RecoveryKind.Inserted, "ins root", tNum, "Module");
        var diagC1 = new RecoveryDiagnostic(6, 7, RecoveryKind.Skipped, "skip c1", tIdent, "Opt");
        var diagA = new RecoveryDiagnostic(0, 1, RecoveryKind.Inserted, "ins a", tNum, "Module");
        var diagD1 = new RecoveryDiagnostic(4, 5, RecoveryKind.Extraneous, "extra d1", tOp, "List");
        var diagB1 = new RecoveryDiagnostic(2, 3, RecoveryKind.Skipped, "skip b1", tIdent, "List");

        parser.AttachDiagnostic(root, diagRoot);
        parser.AttachDiagnostic(c1, diagC1);
        parser.AttachDiagnostic(a, diagA);
        parser.AttachDiagnostic(d1, diagD1);
        parser.AttachDiagnostic(b1, diagB1);

        var derived = parser.DeriveRecoveryDiagnostics(root);

        Assert.AreEqual(5, derived.Count, $"expected exactly the 5 attached diagnostics, got: {Describe(derived)}");

        // Sorted by (StartPos, EndPos): a(0) b1(2) d1(4) c1(6) root(8).
        RecoveryDiagnostic[] expected = [diagA, diagB1, diagD1, diagC1, diagRoot];
        for (var i = 0; i < expected.Length; i++)
        {
            var actual = derived[i];
            var want = expected[i];
            Assert.AreEqual(want.StartPos, actual.StartPos, $"[{i}] StartPos, got: {Describe(derived)}");
            Assert.AreEqual(want.EndPos, actual.EndPos, $"[{i}] EndPos, got: {Describe(derived)}");
            Assert.AreEqual(want.Kind, actual.Kind, $"[{i}] Kind, got: {Describe(derived)}");
            Assert.AreEqual(want.Message, actual.Message, $"[{i}] Message, got: {Describe(derived)}");
            Assert.AreEqual(want.RuleName, actual.RuleName, $"[{i}] RuleName, got: {Describe(derived)}");
            Assert.IsTrue(ReferenceEquals(want.Terminal, actual.Terminal),
                $"[{i}] Terminal must be the exact attached instance, got: {Describe(derived)}");
        }
    }

    // ============ 2. Accumulation: two diagnostics on the SAME node both survive ============
    [TestMethod]
    public void Test_SideTable_Accumulate_TwoOnSameNode()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());

        var node = new TerminalNode("X", 0, 3, 3);
        var root = new SeqNode("Module", [node], 0, 3);

        var d1 = new RecoveryDiagnostic(0, 1, RecoveryKind.Inserted, "first", RecoveryTerminals.Number(), "Rule");
        var d2 = new RecoveryDiagnostic(0, 1, RecoveryKind.Skipped, "second", RecoveryTerminals.Ident(), "Rule");

        parser.AttachDiagnostic(node, d1);
        parser.AttachDiagnostic(node, d2);

        var derived = parser.DeriveRecoveryDiagnostics(root);

        Assert.AreEqual(2, derived.Count, $"both diagnostics on the same node must survive, got: {Describe(derived)}");
        Assert.IsTrue(derived.Any(d => d.Kind == RecoveryKind.Inserted && d.Message == "first"),
            $"first attached diagnostic missing, got: {Describe(derived)}");
        Assert.IsTrue(derived.Any(d => d.Kind == RecoveryKind.Skipped && d.Message == "second"),
            $"second attached diagnostic missing, got: {Describe(derived)}");
    }

    // ============ 3. (StartPos, EndPos) tie-break: same StartPos, different EndPos -> ordered by EndPos ============
    [TestMethod]
    public void Test_SideTable_EndPosTieBreak()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());

        var n1 = new TerminalNode("N1", 0, 1, 1);
        var n2 = new TerminalNode("N2", 2, 3, 1);
        var root = new SeqNode("Module", [n1, n2], 0, 3);

        // Attach in reverse of the expected EndPos order (wider span first) on two nodes.
        var later = new RecoveryDiagnostic(5, 9, RecoveryKind.Skipped, "wider", RecoveryTerminals.Number(), "R");
        var earlier = new RecoveryDiagnostic(5, 6, RecoveryKind.Skipped, "narrower", RecoveryTerminals.Ident(), "R");
        parser.AttachDiagnostic(n2, later);
        parser.AttachDiagnostic(n1, earlier);

        var derived = parser.DeriveRecoveryDiagnostics(root);

        Assert.AreEqual(2, derived.Count, $"expected 2 diagnostics, got: {Describe(derived)}");
        Assert.AreEqual(5, derived[0].StartPos, Describe(derived));
        Assert.AreEqual(6, derived[0].EndPos, $"tie-break: smaller EndPos first, got: {Describe(derived)}");
        Assert.AreEqual("narrower", derived[0].Message, $"tie-break: smaller EndPos first, got: {Describe(derived)}");
        Assert.AreEqual(5, derived[1].StartPos, Describe(derived));
        Assert.AreEqual(9, derived[1].EndPos, $"tie-break: larger EndPos second, got: {Describe(derived)}");
        Assert.AreEqual("wider", derived[1].Message, $"tie-break: larger EndPos second, got: {Describe(derived)}");
    }

    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) \"{d.Message}\" rule={d.RuleName}"));
}
