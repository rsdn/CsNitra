using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class DepthMetricsTests
{
    [TestMethod]
    public void GuardWorkAndEffectiveDepthAreObservableAndReset()
    {
        var parser = new Parser(RecoveryTerminals.Trivia()) { Profile = RecoveryProfile.Test };
        parser.Rules["S"] = [new Seq([new Literal("("), new Ref("S"), new Literal(")")], "Nested"), new Literal("x")];
        parser.BuildTdoppRules();
        parser.Parse(new string('(', 350) + "x" + new string(')', 350), "S", out _);
        Assert.IsTrue(parser.Metrics.GuardFirings > 0);
        Assert.IsTrue(parser.EffectiveMaxParseDepth < parser.InitialMaxParseDepth);
        Assert.AreEqual(parser.InitialMaxParseDepth, parser.Metrics.InitialDepthLimit);
        parser.Parse("x", "S", out _);
        Assert.AreEqual(0, parser.Metrics.GuardFirings);
        Assert.AreEqual(parser.MaxParseDepthReached, parser.Metrics.MaxObservedParseDepth);
        Assert.AreEqual(parser.InitialMaxParseDepth, parser.EffectiveMaxParseDepth);
        Assert.AreEqual(0, parser.Metrics.ProbeGuardFirings);
    }
}
