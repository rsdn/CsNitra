#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class StackFrameTests
{
    private const string Input = "int func() { int var; int var; } int func() { int var; }";

    private sealed class StackRecorder
    {
        public Parser? Parser { get; set; }
        public List<StackFrame[]> Captures { get; } = [];
    }

    private sealed record RecordingTerminal(string Value, StackRecorder Recorder) : Terminal(Value)
    {
        public override int TryMatch(string input, int startPos)
        {
            if (Recorder.Parser is { } parser)
                Recorder.Captures.Add(parser.CurrentStackFrames.ToArray());
            return input.AsSpan(startPos).StartsWith(Value.AsSpan(), StringComparison.Ordinal) ? Value.Length : -1;
        }
    }

    private sealed record ThrowingTerminal(string Value) : Terminal(Value)
    {
        public override int TryMatch(string input, int startPos) => throw new InvalidOperationException("terminal failure");
    }

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

    private static Parser BuildParser(Terminal varTerminal)
    {
        var parser = new Parser(new SpaceTrivia("Trivia"));
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Function"))];
        parser.Rules["Function"] = [
            new Seq([
                new Literal("int"),
                new Literal("func"),
                new Literal("("),
                new Literal(")"),
                new Ref("Block"),
            ], "FunctionDecl"),
            new Seq([
                new Literal("void"),
                new Literal("func"),
                new Literal("("),
                new Literal(")"),
                new Ref("Block"),
            ], "VoidFunction"),
        ];
        parser.Rules["Block"] = [
            new Seq([
                new Literal("{"),
                new ZeroOrMany(new Ref("Statement")),
                new Literal("}"),
            ], "Block"),
            new Seq([
                new Literal("["),
                new ZeroOrMany(new Ref("Statement")),
                new Literal("]"),
            ], "WeirdBlock"),
        ];
        parser.Rules["Statement"] = [
            new Seq([
                new Literal("int"),
                varTerminal,
                new Literal(";"),
            ], "VarDecl"),
            new Seq([
                new Literal("return"),
                new Literal("42"),
                new Literal(";"),
            ], "ReturnStmt"),
        ];
        parser.BuildTdoppRules();
        return parser;
    }

    [TestMethod]
    public void Test_Stack_Depth_And_Locations()
    {
        var recorder = new StackRecorder();
        var varTerminal = new RecordingTerminal("var", recorder);
        var parser = BuildParser(varTerminal);
        recorder.Parser = parser;

        var result = parser.Parse(Input, "Module", out _);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(3, recorder.Captures.Count);

        var stack = recorder.Captures[0];
        Assert.AreEqual(9, stack.Length);
        Assert.AreEqual("Module", stack[0].RuleName);
        Assert.AreEqual(0, stack[0].Precedence);
        Assert.IsTrue(stack[0].Location is RuleFrameLocation { AltIndex: 0 });
        Assert.AreEqual("Module", stack[1].RuleName);
        Assert.IsTrue(stack[1].Location is LoopFrameLocation { LoopKind: "ZeroOrMany", Iteration: 0 });
        Assert.AreEqual("Function", stack[2].RuleName);
        Assert.IsTrue(stack[2].Location is RuleFrameLocation { AltIndex: 0 });
        Assert.AreEqual("Function", stack[3].RuleName);
        Assert.IsTrue(stack[3].Location is SeqFrameLocation { ElementIndex: 4 });
        // Element 4 = Ref("Block"); Expected = First(Block) = {"{", "["}
        var blockExpected = stack[3].Expected!;
        Assert.AreEqual(2, blockExpected.Length);
        var blockKinds = blockExpected.Select(t => t.Kind).ToArray();
        CollectionAssert.Contains(blockKinds, "{");
        CollectionAssert.Contains(blockKinds, "[");
        Assert.AreEqual("Block", stack[4].RuleName);
        Assert.IsTrue(stack[4].Location is RuleFrameLocation { AltIndex: 0 });
        Assert.AreEqual("Block", stack[5].RuleName);
        Assert.IsTrue(stack[5].Location is SeqFrameLocation { ElementIndex: 1 });
        Assert.AreEqual("Block", stack[6].RuleName);
        Assert.IsTrue(stack[6].Location is LoopFrameLocation { LoopKind: "ZeroOrMany", Iteration: 0 });
        Assert.AreEqual("Statement", stack[7].RuleName);
        Assert.IsTrue(stack[7].Location is RuleFrameLocation { AltIndex: 0 });
        Assert.AreEqual("Statement", stack[8].RuleName);
        Assert.IsTrue(stack[8].Location is SeqFrameLocation { ElementIndex: 1 });
        Assert.IsNotNull(stack[8].Expected);
        Assert.AreEqual(1, stack[8].Expected!.Length);
        Assert.IsTrue(ReferenceEquals(stack[8].Expected![0], varTerminal));
    }

    [TestMethod]
    public void Test_Stack_Empty_After_Successful_Parse()
    {
        var recorder = new StackRecorder();
        var parser = BuildParser(new RecordingTerminal("var", recorder));
        recorder.Parser = parser;

        var result = parser.Parse(Input, "Module", out _);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, parser.CurrentStackFrames.Count);
    }

    [TestMethod]
    public void Test_Stack_Empty_After_Exception()
    {
        var parser = BuildParser(new ThrowingTerminal("boom"));

        Assert.ThrowsException<InvalidOperationException>(() => parser.Parse(Input, "Module", out _));

        Assert.AreEqual(0, parser.CurrentStackFrames.Count);
    }

    [TestMethod]
    public void Test_LoopFrameLocation_SecondIteration()
    {
        var recorder = new StackRecorder();
        var parser = BuildParser(new RecordingTerminal("var", recorder));
        recorder.Parser = parser;

        var result = parser.Parse(Input, "Module", out _);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(3, recorder.Captures.Count);

        var secondStatement = recorder.Captures[1];
        Assert.AreEqual(9, secondStatement.Length);
        Assert.IsTrue(secondStatement[1].Location is LoopFrameLocation { LoopKind: "ZeroOrMany", Iteration: 0 });
        Assert.IsTrue(secondStatement[6].Location is LoopFrameLocation { LoopKind: "ZeroOrMany", Iteration: 1 });

        var secondFunction = recorder.Captures[2];
        Assert.AreEqual(9, secondFunction.Length);
        Assert.IsTrue(secondFunction[1].Location is LoopFrameLocation { LoopKind: "ZeroOrMany", Iteration: 1 });
        Assert.IsTrue(secondFunction[6].Location is LoopFrameLocation { LoopKind: "ZeroOrMany", Iteration: 0 });
    }
}
