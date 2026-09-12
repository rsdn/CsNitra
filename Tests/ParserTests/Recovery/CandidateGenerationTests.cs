#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class CandidateGenerationTests
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

    // Recovery отключён: тесты генерируют кандидаты на НЕвосстановленном parse (setup), а не проверяют цикл восстановления.
    private static Parser NewParser()
    {
        var parser = new Parser(new SpaceTrivia("Trivia"));
        parser.MaxRecoveryIterations = 0;
        parser.Rules["Start"] = [new Seq([new Literal("a"), new Literal("b")], "Start")];
        parser.BuildTdoppRules();
        return parser;
    }

    [TestMethod]
    public void Test_S1_Inserts_FailedTerminal_At_E()
    {
        var parser = NewParser();
        parser.Parse("a", "Start", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;

        var candidates = RecoveryEngine.Generate(e, snapshot, "a", parser, Result.Kind.Failure);

        var c = candidates.Single(x => x.Rank == 1);
        Assert.AreEqual("S1:Start:b", c.Id);
        Assert.AreEqual(e, c.Pos);
        Assert.AreEqual(1, c.Cost);
        Assert.AreEqual("b", c.TerminalKind);
        Assert.AreEqual("Start", c.RuleName);
        Assert.AreEqual(RecoveryKind.Inserted, c.Diagnostics[0].Kind);

        c.Apply(parser);
        Assert.IsTrue(parser.Injections.TryGetValue((e, new Literal("b")), out var inj));
        Assert.AreEqual(0, inj.Length);
        Assert.IsFalse(inj.IsSkip);

        c.Rollback(parser);
        Assert.IsFalse(parser.Injections.ContainsKey((e, new Literal("b"))));
    }

    [TestMethod]
    public void Test_S4_Inserts_Suffix_At_EOF()
    {
        var parser = new Parser(new SpaceTrivia("Trivia"));
        parser.MaxRecoveryIterations = 0;
        parser.Rules["Start"] = [new Seq([new Literal("a"), new Ref("X"), new Literal("b")], "Start")];
        parser.Rules["X"] = [new Seq([new Literal("x"), new Literal("y")], "X")];
        parser.BuildTdoppRules();

        var input = "a x";
        parser.Parse(input, "Start", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;
        Assert.AreEqual(input.Length, e);

        var candidates = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure);

        var c = candidates.Single(x => x.Rank == 4);
        Assert.AreEqual("S4:X:EOF", c.Id);
        Assert.AreEqual(input.Length, c.Pos);
        Assert.AreEqual(1, c.Cost);
        Assert.AreEqual("X", c.RuleName);
        Assert.AreEqual(RecoveryKind.Inserted, c.Diagnostics[0].Kind);

        c.Apply(parser);
        Assert.IsTrue(parser.Injections.TryGetValue((input.Length, new Literal("b")), out var inj));
        Assert.AreEqual(0, inj.Length);

        c.Rollback(parser);
        Assert.IsFalse(parser.Injections.ContainsKey((input.Length, new Literal("b"))));
    }

    [TestMethod]
    public void Test_S4_Skips_Nullable_Suffix()
    {
        var parser = new Parser(new SpaceTrivia("Trivia"));
        parser.Rules["Start"] = [new Seq([new Literal("a"), new Literal("b"), new Optional(new Literal("c")), new Literal("d")], "Start")];
        parser.BuildTdoppRules();

        var input = "a";
        parser.Parse(input, "Start", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;

        var candidates = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure);

        var c = candidates.Single(x => x.Rank == 4);
        Assert.AreEqual(1, c.Cost);
        c.Apply(parser);
        Assert.IsTrue(parser.Injections.ContainsKey((input.Length, new Literal("d"))));
        Assert.IsFalse(parser.Injections.ContainsKey((input.Length, new Literal("c"))));
    }

    [TestMethod]
    public void Test_S4_No_Candidate_When_No_Suffix()
    {
        var parser = NewParser();
        var input = "a";
        parser.Parse(input, "Start", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);

        var candidates = RecoveryEngine.Generate(snapshot!.Pos, snapshot, input, parser, Result.Kind.Failure);
        Assert.IsFalse(candidates.Any(x => x.Rank == 4));
    }

    [TestMethod]
    public void Test_Candidates_Sorted_By_Rank_Cost_Pos_Rule_Terminal()
    {
        var parser = NewBracedParser();
        var input = "a { x { y } } b";
        parser.Parse(input, "Start", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;

        var candidates = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure);

        // S1: FailedTerminal i + Expected {i} + FollowSet(Item) {i, }} → i, } (b — только в follow(Body), не в S1).
        // S4: суффиксы c (Item), } (Body), b (Start) → cost 3.
        var ids = candidates.Select(c => c.Id).ToList();
        Assert.AreEqual(4, ids.Count);
        Assert.AreEqual("S1:Item:i", ids[0]);
        Assert.AreEqual("S1:Item:}", ids[1]);
        Assert.AreEqual("S3:Item:}", ids[2]);
        Assert.AreEqual("S4:Item:EOF", ids[3]);
    }

    [TestMethod]
    public void Test_Generate_Is_Deterministic()
    {
        var parser = NewBracedParser();
        var input = "a { x { y } } b";
        parser.Parse(input, "Start", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;

        var first = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure).Select(c => c.Id).ToList();
        var second = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure).Select(c => c.Id).ToList();

        Assert.AreEqual(4, first.Count);
        Assert.IsTrue(first.SequenceEqual(second));
    }

    [TestMethod]
    public void Test_S5_Trailing_Garbage_Absorber()
    {
        var parser = NewParser();
        var input = "abQ";
        var result = parser.Parse(input, "Start", out _);
        Assert.IsTrue(result.TryGetSuccess(out _, out var end));
        Assert.AreEqual(2, end);

        var e = 2;
        var frame = new StackFrame("Start", 0, new RuleFrameLocation(0), [EofTerminal.Instance], null);
        var snapshot = new FailureSnapshot(e, [frame], new Literal("a"), [EofTerminal.Instance]);

        var candidates = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Success);

        var c = candidates.Single(x => x.Rank == 5);
        Assert.AreEqual("S5:Start:Trailing", c.Id);
        Assert.AreEqual(e, c.Pos);
        Assert.AreEqual(2, c.Cost);
        Assert.AreEqual(RecoveryKind.Skipped, c.Diagnostics[0].Kind);
        Assert.AreEqual(e, c.Diagnostics[0].StartPos);
        Assert.AreEqual(input.Length, c.Diagnostics[0].EndPos);

        c.Apply(parser);
        Assert.IsTrue(parser.Injections.TryGetValue((e, new Literal("a")), out var inj));
        Assert.AreEqual(1, inj.Length);
        Assert.IsTrue(inj.IsSkip);
        Assert.AreEqual("Trailing", inj.NodeKind);

        c.Rollback(parser);
        Assert.IsFalse(parser.Injections.ContainsKey((e, new Literal("a"))));
    }

    private static Parser NewBracedParser()
    {
        var parser = new Parser(new SpaceTrivia("Trivia"));
        parser.MaxRecoveryIterations = 0;
        parser.Rules["Start"] = [new Seq([new Literal("a"), new Ref("Body"), new Literal("b")], "Start")];
        parser.Rules["Body"] = [new Seq([new Literal("{"), new ZeroOrMany(new Ref("Item")), new Literal("}")], "Body")];
        parser.Rules["Item"] = [new Seq([new Literal("i"), new Literal("c")], "Item")];
        parser.BuildTdoppRules();
        return parser;
    }

    [TestMethod]
    public void Test_S3_Finds_Terminator_With_Nesting()
    {
        var parser = NewBracedParser();
        var input = "a { x { y } } b";
        parser.Parse(input, "Start", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;
        Assert.AreEqual(4, e);

        var candidates = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure);

        var c = candidates.Single(x => x.Rank == 3);
        Assert.AreEqual("S3:Item:}", c.Id);
        Assert.AreEqual(12, c.Pos);
        Assert.AreEqual(4, c.Cost);
        Assert.AreEqual(RecoveryKind.Skipped, c.Diagnostics[0].Kind);
        Assert.AreEqual(e, c.Diagnostics[0].StartPos);
        Assert.AreEqual(12, c.Diagnostics[0].EndPos);

        c.Apply(parser);
        Assert.IsTrue(parser.Injections.TryGetValue((e, new Literal("i")), out var inj));
        Assert.AreEqual(8, inj.Length);
        Assert.IsTrue(inj.IsSkip);

        c.Rollback(parser);
        Assert.IsFalse(parser.Injections.ContainsKey((e, new Literal("i"))));
    }

    [TestMethod]
    public void Test_S3_Simple_Terminator()
    {
        var parser = NewBracedParser();
        var input = "a { x } b";
        parser.Parse(input, "Start", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;
        Assert.AreEqual(4, e);

        var candidates = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure);

        var c = candidates.Single(x => x.Rank == 3);
        Assert.AreEqual("S3:Item:}", c.Id);
        Assert.AreEqual(6, c.Pos);
        Assert.AreEqual(1, c.Cost);

        c.Apply(parser);
        Assert.IsTrue(parser.Injections.TryGetValue((e, new Literal("i")), out var inj));
        Assert.AreEqual(2, inj.Length);
        Assert.IsTrue(inj.IsSkip);
    }

    [TestMethod]
    public void Test_S5_Only_For_Success_Before_EOF()
    {
        var parser = NewParser();
        parser.Parse("a", "Start", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;

        var asFailure = RecoveryEngine.Generate(e, snapshot, "a", parser, Result.Kind.Failure);
        Assert.IsFalse(asFailure.Any(x => x.Rank == 5));

        var eofInput = "ab";
        var asSuccessAtEof = RecoveryEngine.Generate(eofInput.Length, snapshot, eofInput, parser, Result.Kind.Success);
        Assert.IsFalse(asSuccessAtEof.Any(x => x.Rank == 5));
    }

    [TestMethod]
    public void Test_S1_Matching_Terminal_At_E_No_Candidate()
    {
        var parser = NewParser();
        parser.Parse("aa", "Start", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;
        Assert.AreEqual(1, e);

        var top = snapshot.Stack[^1];
        var frame = top with { Options = new RecoveryOptions { TryInsert = [new Literal("a"), new Literal("c")] } };
        var rebuilt = snapshot with { Stack = snapshot.Stack[..^1].Concat([frame]).ToArray() };

        var candidates = RecoveryEngine.Generate(e, rebuilt, "aa", parser, Result.Kind.Failure);

        Assert.IsFalse(candidates.Any(x => x.TerminalKind == "a"));
        var c = candidates.Single(x => x.TerminalKind == "c");
        Assert.AreEqual(0, c.Rank);
        Assert.AreEqual("S1:Start:c", c.Id);
        Assert.AreEqual("S1:Start:b", candidates.Single(x => x.TerminalKind == "b").Id);
    }
}
