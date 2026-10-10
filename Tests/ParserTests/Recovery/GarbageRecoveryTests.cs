using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class GarbageRecoveryTests
{
    [DataTestMethod]
    [DataRow("a; ### b;")]
    [DataRow("a; @@@b;")]
    public void LocalGarbagePreservesNextDeclarationWithoutBroadGeneration(string input)
    {
        var parser = new Parser(RecoveryTerminals.Trivia()) { Profile = RecoveryProfile.Test };
        parser.Rules["Item"] = [new Seq([new Literal("a"), new Literal(";")], "Declaration"),
            new Seq([new Literal("b"), new Literal(";")], "Declaration")];
        parser.Rules["Unrelated"] = [new Literal("#")];
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Item"), "Items")];
        parser.BuildTdoppRules();
        Assert.IsTrue(parser.Parse(input, "Module", out _).TryGetSuccess(out var root, out var end));
        Assert.AreEqual(input.Length, end);
        RecoveryStructureTests.AssertCoverage(root, input.Length);
        Assert.AreEqual(2, RecoveryStructureTests.Walk(root).OfType<SeqNode>()
            .Count(n => n.Kind == "Declaration"));
        Assert.AreEqual(0, parser.EngineGenerateCalls, "Local repair should avoid broad candidate generation.");
        Assert.IsFalse(parser.RecoveryDiagnostics.Any(d => d.Kind == RecoveryKind.Unrecovered));
    }
    [DataTestMethod]
    [DataRow(64, true)]
    [DataRow(65, false)]
    public void LocalScanHasACharacterBound(int length, bool local)
    {
        var parser = new Parser(RecoveryTerminals.Trivia()) { Profile = RecoveryProfile.Test };
        parser.Rules["Item"] = [new Seq([new Literal("b"), new Literal(";")], "Declaration")];
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Item"), "Items")];
        parser.BuildTdoppRules();
        var input = "b; " + new string('#', length) + "b;";
        Assert.IsTrue(parser.Parse(input, "Module", out _).TryGetSuccess(out var root, out var end));
        Assert.AreEqual(input.Length, end);
        RecoveryStructureTests.AssertCoverage(root, input.Length);
        Assert.AreEqual(local ? 1 : 0, parser.Metrics.LocalGarbageRepairs);
    }

    [DataTestMethod]
    [DataRow("###")]
    [DataRow("a; ###")]
    [DataRow("a; b;")]
    public void EofAndValidInputRemainCovered(string input)
    {
        var parser = new Parser(RecoveryTerminals.Trivia()) { Profile = RecoveryProfile.Test };
        parser.Rules["Item"] = [new Seq([new Literal("a"), new Literal(";")], "Declaration"),
            new Seq([new Literal("b"), new Literal(";")], "Declaration")];
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Item"), "Items")];
        parser.BuildTdoppRules();
        Assert.IsTrue(parser.Parse(input, "Module", out _).TryGetSuccess(out var root, out var end));
        Assert.AreEqual(input.Length, end);
        RecoveryStructureTests.AssertCoverage(root, input.Length);
        Assert.AreEqual(0, parser.Metrics.LocalGarbageRepairs);
    }
}
