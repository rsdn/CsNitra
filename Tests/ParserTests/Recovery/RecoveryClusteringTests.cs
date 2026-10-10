using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class RecoveryClusteringTests
{
    [TestMethod]
    public void DenseSkippedRegionsClusterAndParserReuseResets()
    {
        var profile = RecoveryProfile.Test with { EnableLocalGarbageRecovery = false,
            EnableRecoveryClustering = true, ClusterWorkThreshold = 1 };
        var parser = Create(profile);
        var input = string.Concat(Enumerable.Repeat("a $ ; ", 8));
        Assert.IsTrue(parser.Parse(input, "Module", out _).TryGetSuccess(out var root, out var end));
        Assert.AreEqual(input.Length, end);
        RecoveryStructureTests.AssertCoverage(root, input.Length);
        Assert.IsTrue(parser.Metrics.ClusteredPoints > 0);
        parser.Parse("a b;", "Module", out _);
        Assert.AreEqual(0, parser.Metrics.ClusteredPoints);
    }

    [DataTestMethod]
    [DataRow("a $ ; a b; a $ ;")]
    [DataRow("a $ ;                                                                   a $ ;")]
    public void IntactItemsAndDistanceResetACluster(string input)
    {
        var parser = Create(RecoveryProfile.Test with { EnableLocalGarbageRecovery = false,
            EnableRecoveryClustering = true, ClusterWorkThreshold = 1 });
        Assert.IsTrue(parser.Parse(input, "Module", out _).TryGetSuccess(out var root, out var end));
        Assert.AreEqual(input.Length, end);
        RecoveryStructureTests.AssertCoverage(root, input.Length);
        Assert.AreEqual(0, parser.Metrics.ClusteredPoints);
        Assert.IsTrue(parser.Metrics.ClusterResets > 0);
    }

    [TestMethod]
    public void InsertionsAndRejectedCandidatesDoNotEstablishSkippedRegions()
    {
        var parser = Create(RecoveryProfile.Test with { EnableRecoveryClustering = true, ClusterWorkThreshold = 1 });
        parser.Parse("a ; a ;", "Module", out _);
        Assert.AreEqual(0, parser.Metrics.ClusteredPoints);
        Assert.IsTrue(parser.Metrics.TotalRollback > 0);
    }

    [TestMethod]
    public void DenseFixtureRetainsTheSameRealDeclarationsWhenOptedIn()
    {
        Assert.IsFalse(RecoveryProfile.Ide.EnableRecoveryClustering);
        var input = string.Concat(Enumerable.Repeat("a $ ; ", 8));
        foreach (var enabled in new[] { false, true })
        {
            var parser = Create(RecoveryProfile.Test with { EnableLocalGarbageRecovery = false,
                EnableRecoveryClustering = enabled, ClusterWorkThreshold = 1 });
            Assert.IsTrue(parser.Parse(input, "Module", out _).TryGetSuccess(out var root, out _));
            Assert.AreEqual(8, RecoveryStructureTests.Walk(root).OfType<TerminalNode>()
                .Count(n => !n.IsRecovery && n.ContentLength == 1 && input[n.StartPos] == 'a'));
        }
    }

    private static Parser Create(RecoveryProfile profile)
    {
        var parser = new Parser(RecoveryTerminals.Trivia()) { Profile = profile };
        parser.Rules["Item"] = [new Seq([new Literal("a"), new Literal("b"), new Literal(";")], "Declaration")];
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Item"), "Declarations")];
        parser.BuildTdoppRules();
        return parser;
    }
}
