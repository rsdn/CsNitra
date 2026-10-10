using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class ActiveAlternativeContextTests
{
    [DataTestMethod]
    [DataRow("p x $", "z", "r")]
    [DataRow("q x $", "r", "z")]
    public void NestedSequenceUsesItsOwnSuffix(string input, string active, string inactive)
    {
        var parser = Create();
        parser.Parse(input, "S", out _);
        var snapshot = parser.LastSnapshot!;
        Assert.IsNotNull(snapshot);
        var suffixes = RecoveryEngine.GetSuffixObligationFirsts(snapshot, parser,
            snapshot.Stack.Length - 1, respectAltIndex: true).Select(x => x.T.Kind).ToArray();
        CollectionAssert.Contains(suffixes, active);
        CollectionAssert.DoesNotContain(suffixes, inactive);
        var expected = RecoveryEngine.BuildExpectedSet(snapshot, parser).Select(t => t.Kind).ToArray();
        CollectionAssert.Contains(expected, active);
        CollectionAssert.DoesNotContain(expected, inactive);
    }

    [DataTestMethod]
    [DataRow("p x $ r z end", "z")]
    [DataRow("q x $ z r end", "r")]
    public void SkipDoesNotStopAtInactiveSuffix(string input, string active)
    {
        var parser = Create();
        parser.Parse(input, "S", out _);
        var snapshot = parser.LastSnapshot!;
        var candidates = RecoveryEngine.Generate(snapshot.Pos, snapshot, input, parser,
            Result.Kind.Failure, "S", 0, 0);
        var expectedPosition = input.IndexOf(active, snapshot.Pos, StringComparison.Ordinal);
        foreach (var candidate in candidates.Where(c => c.Rank is 3 or 6))
            Assert.AreEqual(expectedPosition, candidate.Pos);
        Assert.IsTrue(candidates.Any(c => c.Rank == 6));
    }

    [TestMethod]
    public void RecursiveSequenceKeepsInnerAndOuterOccurrences()
    {
        var parser = Create();
        parser.Rules["S"] = [new Seq([new Literal("("), new Ref("S"), new Literal(")")], "Group"),
            new Seq([new Literal("x"), new Literal("y"), new Literal("z")], "Leaf")];
        parser.BuildTdoppRules();
        parser.Parse("(x $", "S", out _);
        var snapshot = parser.LastSnapshot!;
        var suffixes = RecoveryEngine.GetSuffixObligationFirsts(snapshot, parser,
            snapshot.Stack.Length - 1, true).Select(x => x.T.Kind).ToArray();
        CollectionAssert.Contains(suffixes, "z");
        CollectionAssert.Contains(suffixes, ")");
    }

    private static Parser Create()
    {
        var parser = new Parser(RecoveryTerminals.Trivia()) { Profile = RecoveryProfile.Compiler };
        parser.Rules["S"] = [Alternative("p", "z"), Alternative("q", "r")];
        parser.BuildTdoppRules();
        return parser;

        static Rule Alternative(string prefix, string suffix) => new Seq(
            [new Literal(prefix), new RecoveryRule(new Seq(
                [new Literal("x"), new Literal("y"), new Literal(suffix)], "Nested")), new Literal("end")], "Outer");
    }
}
