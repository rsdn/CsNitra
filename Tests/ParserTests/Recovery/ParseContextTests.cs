#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
namespace Recovery;

[TestClass]
public class ParseContextTests
{
    private Parser CreateSimpleParser()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));
        parser.Rules["Module"] = new Rule[]
        {
            new ZeroOrMany(new Ref("Statement")),
        };
        parser.Rules["Statement"] = new Rule[]
        {
            new Seq(new Rule[] { new Literal("a"), new Literal("b"), new Literal("c") }, "Statement"),
        };
        parser.MaxRecoveryIterations = 0;
        parser.BuildTdoppRules();
        return parser;
    }

    [TestMethod]
    public void Test_Context_InPartial()
    {
        var parser = CreateSimpleParser();

        // "ab" — missing "c" — should parse "a" and "b" successfully but fail on "c"
        var result = parser.Parse("ab", "Module", out _);

        // The parse should fail (input not fully consumed), but partial results have context
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void Test_PartialResult_HasContext()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));
        parser.MaxRecoveryIterations = 0;
        parser.Rules["Expr"] = new Rule[]
        {
            new Seq(new Rule[] { new Literal("x"), new Literal("y") }, "Expr"),
        };
        parser.BuildTdoppRules();

        // "xz" — 'y' is missing — Seq will fail on 'y'
        var result = parser.Parse("xz", "Expr", out _);

        // Parse fails, but a Partial should have been produced with context
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void Test_Success_NoContext()
    {
        var success = Result.Success(
            new TerminalNode("Test", 0, 1, 1),
            newPos: 1,
            maxFailPos: 1);

        Assert.AreEqual(Result.Kind.Success, success.ResultKind);
        Assert.IsFalse(success.TryGetPartial(out _, out _, out var ctx));
    }

    [TestMethod]
    public void Test_Failure_NoContext()
    {
        var failure = Result.Failure(5);

        Assert.AreEqual(Result.Kind.Failure, failure.ResultKind);
        Assert.IsFalse(failure.TryGetPartial(out _, out _, out _));
    }

    [TestMethod]
    public void Test_Partial_WithContext()
    {
        var node = new TerminalNode("Test", 0, 1, 1);
        var context = new ParseContext(
            "TestRule",
            new SeqFrameLocation(2),
            new Terminal[] { new Literal("expected") },
            null);

        var partial = Result.Partial(node, 1, 5, context);

        Assert.AreEqual(Result.Kind.Partial, partial.ResultKind);
        Assert.IsTrue(partial.TryGetPartial(out _, out _, out var ctx));
        Assert.IsNotNull(ctx);
        Assert.AreEqual("TestRule", ctx.RuleName);
        Assert.AreEqual(new SeqFrameLocation(2), ctx.Location);
    }

    [TestMethod]
    public void Test_Partial_WithoutContext()
    {
        var node = new TerminalNode("Test", 0, 1, 1);
        var partial = Result.Partial(node, 1, 5);

        Assert.AreEqual(Result.Kind.Partial, partial.ResultKind);
        Assert.IsTrue(partial.TryGetPartial(out _, out _, out var ctx));
        Assert.IsNull(ctx);
    }

    [TestMethod]
    public void Test_TryGetPartial_WithContext()
    {
        var node = new TerminalNode("Test", 0, 1, 1);
        var context = new ParseContext(
            "TestRule",
            new LoopFrameLocation("OneOrMany", 3),
            Array.Empty<Terminal>(),
            null);

        var partial = Result.Partial(node, 1, 5, context);

        Assert.IsTrue(partial.TryGetPartial(out var outNode, out var newPos, out var outContext));
        Assert.IsNotNull(outNode);
        Assert.AreEqual(1, newPos);
        Assert.IsNotNull(outContext);
        Assert.AreEqual("TestRule", outContext.RuleName);
        Assert.AreEqual(new LoopFrameLocation("OneOrMany", 3), outContext.Location);
    }

    [TestMethod]
    public void Test_TryGetPartial_Success_ReturnsNullContext()
    {
        var node = new TerminalNode("Test", 0, 1, 1);
        var success = Result.Success(node, 1, 1);

        Assert.IsFalse(success.TryGetPartial(out _, out _, out var context));
        Assert.IsNull(context);
    }

    [TestMethod]
    public void Test_NoAllocationForSuccess()
    {
        var success = Result.Success(
            new TerminalNode("Test", 0, 1, 1),
            newPos: 1,
            maxFailPos: 1);

        Assert.IsFalse(success.TryGetPartial(out _, out _, out _));
    }

    [TestMethod]
    public void Test_NoAllocationForFailure()
    {
        var failure = Result.Failure(5);
        Assert.IsFalse(failure.TryGetPartial(out _, out _, out _));
    }

    [TestMethod]
    public void Test_FrameLocation_Types()
    {
        var seqLoc = new SeqFrameLocation(42);
        Assert.AreEqual(42, seqLoc.ElementIndex);

        var loopLoc = new LoopFrameLocation("ZeroOrMany", 7);
        Assert.AreEqual("ZeroOrMany", loopLoc.LoopKind);
        Assert.AreEqual(7, loopLoc.Iteration);

        var ruleLoc = new RuleFrameLocation(3);
        Assert.AreEqual(3, ruleLoc.AltIndex);
    }

    [TestMethod]
    public void Test_Expected_Terminals()
    {
        var expected = new Terminal[] { new Literal(";"), new Literal("}") };
        var ctx = new ParseContext("TestRule", new SeqFrameLocation(1), expected, null);

        Assert.AreEqual(2, ctx.Expected.Length);
        Assert.AreEqual(";", ctx.Expected[0].Kind);
        Assert.AreEqual("}", ctx.Expected[1].Kind);
    }

    [TestMethod]
    public void Test_Nested_Contexts()
    {
        // Simulating: Seq -> Loop -> Prefix
        var outerCtx = new ParseContext(
            "Seq",
            new SeqFrameLocation(2),
            Array.Empty<Terminal>(),
            null);

        var innerCtx = new ParseContext(
            "Loop",
            new LoopFrameLocation("OneOrMany", 5),
            Array.Empty<Terminal>(),
            null);

        var innermostCtx = new ParseContext(
            "Prefix",
            new RuleFrameLocation(0),
            Array.Empty<Terminal>(),
            null);

        Assert.IsNotNull(outerCtx);
        Assert.IsNotNull(innerCtx);
        Assert.IsNotNull(innermostCtx);
        Assert.AreEqual("Seq", outerCtx.RuleName);
        Assert.AreEqual("Loop", innerCtx.RuleName);
        Assert.AreEqual("Prefix", innermostCtx.RuleName);
    }

    [TestMethod]
    public void Test_RecoveryOptions()
    {
        var options = new RecoveryOptions
        {
            Terminators = new Terminal[] { new Literal("}"), new Literal(";") }
        };

        var ctx = new ParseContext(
            "TestRule",
            new SeqFrameLocation(0),
            Array.Empty<Terminal>(),
            options);

        Assert.IsNotNull(ctx.Options);
        Assert.AreEqual(2, ctx.Options.Terminators!.Length);
    }

    [TestMethod]
    public void Test_ParseSeq_ProducesPartialWithContext()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));

        // Grammar: Expr = "x" "y" "z"
        parser.MaxRecoveryIterations = 0;
        parser.Rules["Expr"] = new Rule[]
        {
            new Seq(new Rule[] { new Literal("x"), new Literal("y"), new Literal("z") }, "Expr"),
        };

        parser.BuildTdoppRules();

        // "x!missing!z" — "x" matches, "y" fails at "!", error at position 1
        var result = parser.Parse("x!missing!z", "Expr", out _);

        // Parse will fail, but partial results should exist
        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(parser.ErrorPos >= 0);
    }

    [TestMethod]
    public void Test_ParseOneOrMany_ProducesPartialWithContext()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));

        parser.MaxRecoveryIterations = 0;
        parser.Rules["Expr"] = new Rule[]
        {
            new Seq(new Rule[] {
                new OneOrMany(new Literal("x")),
                new Literal("y"),
            }, "Expr"),
        };

        parser.BuildTdoppRules();

        // "xxy" — "xx" from OneOrMany, then "y" matches. Should succeed.
        var result = parser.Parse("xxy", "Expr", out _);
        Assert.IsTrue(result.IsSuccess);

        // "xxz" — "xx" from OneOrMany, then "y" fails on "z"
        var result2 = parser.Parse("xxz", "Expr", out _);
        Assert.IsFalse(result2.IsSuccess);
    }

    [TestMethod]
    public void Test_ParseZeroOrMany_ProducesPartialWithContext()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));

        parser.Rules["Expr"] = new Rule[]
        {
            new Seq(new Rule[] {
                new ZeroOrMany(new Literal("x")),
                new Literal("y"),
            }, "Expr"),
        };

        parser.BuildTdoppRules();

        // "xxy" — "xx" from ZeroOrMany, "y" matches
        var result = parser.Parse("xxy", "Expr", out _);
        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void Test_Predicate_Isolation()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));

        // Grammar: Expr = &"a" "b"
        // If "a" doesn't match, predicate fails, Expr fails.
        // ErrorPos should NOT be affected by the predicate.
        parser.Rules["Expr"] = new Rule[]
        {
            new Seq(new Rule[] {
                new AndPredicate(new Literal("a")),
                new Literal("b"),
            }, "Expr"),
        };

        parser.BuildTdoppRules();

        // Input "x" — predicate &"a" fails, so Expr fails.
        // ErrorPos should be at position 0 (where the parse started)
        var result = parser.Parse("x", "Expr", out _);
        Assert.IsFalse(result.IsSuccess);
    }

     [TestMethod]
    public void Test_WithPrefixOnly_PreservesContext()
    {
        var node = new TerminalNode("Test", 0, 1, 1);
        var context = new ParseContext(
            "TestRule",
            new SeqFrameLocation(0),
            Array.Empty<Terminal>(),
            null);

        var partial = Result.Partial(node, 1, 5, context);
        var wrapped = new Result().WithPrefixOnly(partial);

        Assert.AreEqual(Result.Kind.Partial, wrapped.ResultKind);
        Assert.IsTrue(wrapped.TryGetPartial(out _, out _, out var wrappedCtx));
        Assert.IsNotNull(wrappedCtx);
    }

    [TestMethod]
    public void Test_Result_Partial_Equality()
    {
        var node = new TerminalNode("Test", 0, 1, 1);
        var context = new ParseContext(
            "TestRule",
            new SeqFrameLocation(0),
            Array.Empty<Terminal>(),
            null);

        var partial1 = Result.Partial(node, 1, 5, context);
        var partial2 = Result.Partial(node, 1, 5, context);

        Assert.AreEqual(partial1, partial2);
    }
}
#endif
