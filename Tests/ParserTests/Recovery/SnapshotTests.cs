#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
namespace Recovery;

[TestClass]
public sealed class SnapshotTests
{
    private sealed record SpaceTrivia(string Name) : Terminal(Name)
    {
        public override int TryMatch(string input, int startPos)
        {
            var len = 0;
            while (startPos + len < input.Length && input[startPos + len] == ' ')
                len++;
            return len;
        }
    }

    // Recovery отключён: тесты снимают снимок на НЕвосстановленном parse (setup), а не проверяют цикл восстановления.
    private static Parser NewParser()
    {
        var parser = new Parser(new SpaceTrivia("Trivia"));
        parser.MaxRecoveryIterations = 0;
        return parser;
    }

    [TestMethod]
    public void Test_Snapshot_At_Farthest_Mismatch()
    {
        var parser = NewParser();
        var c = new Literal("c");
        parser.Rules["Start"] = [new Seq([new Literal("a"), new Literal("b"), c], "Start")];
        parser.BuildTdoppRules();

        var result = parser.Parse("ab", "Start", out _);

        Assert.IsFalse(result.IsSuccess);
        var snap = parser.LastSnapshot;
        Assert.IsNotNull(snap);
        Assert.AreEqual(parser.ErrorPos, snap.Pos);
        Assert.AreEqual(2, snap.Pos);
        Assert.IsTrue(ReferenceEquals(snap.FailedTerminal, c));
        Assert.IsTrue(snap.Stack.Length > 0);
        var top = snap.Stack[snap.Stack.Length - 1];
        Assert.AreEqual("Start", top.RuleName);
        Assert.IsTrue(top.Location is SeqFrameLocation { ElementIndex: 2 });
        Assert.IsTrue(snap.Expected.Contains(c));
    }

    [TestMethod]
    public void Test_Snapshot_At_Farthest_Not_First()
    {
        var parser = NewParser();
        parser.Rules["Start"] =
        [
            new Seq([new Literal("x")], "AltX"),
            new Seq([new Literal("a"), new Literal("b"), new Literal("c")], "AltABC"),
        ];
        parser.BuildTdoppRules();

        var result = parser.Parse("abQ", "Start", out _);

        Assert.IsFalse(result.IsSuccess);
        var snap = parser.LastSnapshot;
        Assert.IsNotNull(snap);
        Assert.AreEqual(2, snap.Pos);
        Assert.AreEqual(parser.ErrorPos, snap.Pos);
        Assert.AreEqual("c", snap.FailedTerminal.Kind);
    }

    private static Parser BuildAndPredicateParser()
    {
        var parser = NewParser();
        parser.Rules["Start"] =
        [
            new Seq(
                [
                    new Literal("a"),
                    new AndPredicate(new Seq([new Literal("b"), new Literal("c")], "bc")),
                    new Literal("y"),
                ],
                "Branch1"),
            new Seq([new Literal("a"), new Literal("z")], "Branch2"),
        ];
        parser.MaxRecoveryIterations = 0;
        parser.BuildTdoppRules();
        return parser;
    }

    [TestMethod]
    public void Test_Speculative_AndPredicate_DoesNotMove_ErrorPos()
    {
        var parser = BuildAndPredicateParser();

        var result = parser.Parse("a b Q", "Start", out _);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(2, parser.ErrorPos);
        var snap = parser.LastSnapshot;
        Assert.IsNotNull(snap);
        Assert.AreEqual(2, snap.Pos);
        Assert.AreEqual("z", snap.FailedTerminal.Kind);
    }

    [TestMethod]
    public void Test_Speculative_AndPredicate_DoesNotPollute_Expected()
    {
        var parser = BuildAndPredicateParser();

        var result = parser.Parse("a b Q", "Start", out _);

        Assert.IsFalse(result.IsSuccess);
        var error = parser.ErrorInfo;
        Assert.IsNotNull(error);
        var kinds = error.Expecteds.Select(t => t.Kind).ToArray();
        Assert.IsTrue(kinds.Contains("z"));
        Assert.IsFalse(kinds.Contains("b"));
        Assert.IsFalse(kinds.Contains("c"));
        Assert.IsFalse(kinds.Contains("y"));
    }

    [TestMethod]
    public void Test_Speculative_NotPredicate_DoesNotMove_ErrorPos()
    {
        var parser = NewParser();
        parser.Rules["Start"] =
        [
            new Seq(
                [
                    new Literal("a"),
                    new NotPredicate(new Seq([new Literal("b"), new Literal("c")], "bc")),
                    new Literal("x"),
                ],
                "Start"),
        ];
        parser.BuildTdoppRules();

        var result = parser.Parse("a b Q", "Start", out _);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(2, parser.ErrorPos);
        var snap = parser.LastSnapshot;
        Assert.IsNotNull(snap);
        Assert.AreEqual(2, snap.Pos);
        Assert.AreEqual("x", snap.FailedTerminal.Kind);
    }

    [TestMethod]
    public void Test_Successful_Parse_Leaves_No_Snapshot()
    {
        var parser = NewParser();
        parser.Rules["Start"] = [new Seq([new Literal("a"), new Literal("b"), new Literal("c")], "Start")];
        parser.BuildTdoppRules();

        var result = parser.Parse("abc", "Start", out _);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(parser.LastSnapshot);
    }
}

#endif
