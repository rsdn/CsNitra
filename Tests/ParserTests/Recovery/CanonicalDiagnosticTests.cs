using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class CanonicalDiagnosticTests
{
    [DataTestMethod]
    [DataRow("\n")]
    [DataRow("\r\n")]
    public void CanonicalPlacementIsSharedByAllQueries(string newline)
    {
        var parser = new Parser(RecoveryTerminals.Trivia()) { Profile = RecoveryProfile.Test };
        parser.Rules["Stmt"] = [new Seq([new Literal("a"), new Literal(":"), RecoveryTerminals.Number(),
            new Literal(";")], "Stmt")];
        parser.Rules["Module"] = [new Seq([new Literal("{"), new Ref("Stmt"), new Literal("}")], "Module")];
        parser.BuildTdoppRules();
        var input = "{" + newline + "a: " + newline + ";}";
        Assert.IsTrue(parser.Parse(input, "Module", out _).TryGetSuccess(out var root, out _));
        var derived = parser.DeriveRecoveryDiagnostics(root);
        var inserted = parser.RecoveryDiagnostics.Single(d => d.Kind == RecoveryKind.Inserted);
        Assert.AreEqual(input.IndexOf(':') + 1, inserted.StartPos);
        Assert.IsTrue(derived.Any(d => ReferenceEquals(d, inserted)));
        var missing = RecoveryStructureTests.Walk(root).OfType<TerminalNode>()
            .Single(n => n.IsRecovery && n.StartPos == n.EndPos);
        Assert.AreEqual(input.IndexOf(';'), missing.StartPos);
        Assert.AreSame(inserted, parser.DeriveRecoveryDiagnostics(missing).Single());
    }

    [TestMethod]
    public void OftenMissedRepairCannotBeSilentlyClean()
    {
        var parser = new Parser(RecoveryTerminals.Trivia()) { Profile = RecoveryProfile.Test };
        parser.Rules["S"] = [new Seq([new Literal("a"), new OftenMissed(new Literal(";")), new Literal("b")], "S")];
        parser.BuildTdoppRules();
        Assert.IsTrue(parser.Parse("a b", "S", out _).TryGetSuccess(out var root, out var end));
        Assert.AreEqual(3, end);
        var inserted = parser.RecoveryDiagnostics.Single(d => d.Kind == RecoveryKind.Inserted);
        Assert.AreSame(inserted, parser.DeriveRecoveryDiagnostics(root).Single());
    }
}
