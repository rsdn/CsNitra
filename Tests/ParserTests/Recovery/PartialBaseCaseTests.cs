#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class PartialBaseCaseTests
{
    private sealed record EpsilonCounter(int[] Counter) : Terminal("eps")
    {
        public override int TryMatch(string input, int startPos)
        {
            Counter[0]++;
            return 0;
        }
    }

    private static Parser BuildVarDeclParser()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));
        parser.Rules["VarDecl"] = [new Seq([new Literal("int"), new Literal("x"), new Literal(";")], "VarDecl")];
        parser.BuildTdoppRules();
        return parser;
    }

    // ============ 1. Базовый случай Partial в ParseSeq ============

    [TestMethod]
    public void Test_BaseCase_ProducesPartial()
    {
        var parser = BuildVarDeclParser();
        parser.Parse("intx", "VarDecl", out _);

        Assert.IsNotNull(parser.LastPartial);
        Assert.IsTrue(parser.LastPartial!.Value.TryGetPartial(out var node, out var newPos, out var ctx));
        Assert.AreEqual(4, newPos);
        Assert.IsNotNull(ctx);
        Assert.AreEqual("VarDecl", ctx.RuleName);
        Assert.AreEqual(new SeqFrameLocation(2), ctx.Location);
        Assert.AreEqual(1, ctx.Expected.Length);
        Assert.AreEqual(";", ctx.Expected[0].Kind);
        Assert.IsTrue(node is SeqNode);
        var seqNode = (SeqNode)node;
        Assert.AreEqual(2, seqNode.Elements.Count);
    }

    // ============ 2. ε-цикл не виснет ============

    [TestMethod]
    public void Test_EpsilonLoop_DoesNotHang()
    {
        var counter = new int[1];
        var parser = new Parser(new EmptyTerminal("Trivia"));
        parser.Rules["Start"] = [new ZeroOrMany(new EpsilonCounter(counter))];
        parser.BuildTdoppRules();
        parser.Parse("abc", "Start", out _);
        // A10 contract change (precedent: checklist 1.7d — collateral test updated when the contract
        // legitimately changed): the S3/S6 scans now probe ALL grammar terminals at every position where
        // the stop-set does not match (token-jump), so EpsilonCounter.TryMatch is called more often than
        // at HEAD (the old bound 2 encoded HEAD's probe set = stop-set only). Exact bounded count for
        // "abc" (len 3), measured: 1 main-parse call at pos 0 + 2 S6-scan probe calls at pos 1..len-1
        // (pos 3 is the EOF stop, not probed) = 3; the re-parse after the S6 memo patch adds 0 (the memo
        // hit short-circuits the rule). Bound stays finite and tied to input length: 1 + (len - 1).
        // Intent preserved: a finite bound proves the ε-loop does not hang.
        Assert.IsTrue(counter[0] <= 3);
    }

    // ============ 3. Тай-брейк: Success бьёт Partial при равной длине ============

    [TestMethod]
    public void Test_TieBreak_SuccessBeatsPartialEqualLength()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));
        parser.Rules["Expr"] = [
            new Literal("a"),
            new Literal("a+bb"),
            new Literal("b"),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100), new Seq([new Literal("b"), new Literal("c")], "Inner")], "Add"),
        ];
        parser.BuildTdoppRules();
        var result = parser.Parse("a+bb", "Expr", out _);
        Assert.IsTrue(result.IsSuccess);
    }

    // ============ 4. Тай-брейк: Partial бьёт Success при большей длине ============

    [TestMethod]
    public void Test_TieBreak_PartialBeatsSuccessGreaterLength()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));
        parser.Rules["Expr"] = [
            new Literal("a"),
            new Literal("a+"),
            new Literal("b"),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100), new Seq([new Literal("b"), new Literal("c")], "Inner")], "Add"),
        ];
        parser.BuildTdoppRules();
        parser.Parse("a+bb", "Expr", out _);
        Assert.IsNotNull(parser.LastPartial);
        Assert.IsTrue(parser.LastPartial!.Value.TryGetPartial(out _, out var newPos, out _));
        Assert.AreEqual(4, newPos);
    }

    // ============ 5. Корректный вход не меняется (I6) ============

    [TestMethod]
    public void Test_ValidInput_NoPartial()
    {
        var parser = BuildVarDeclParser();
        var result = parser.Parse("intx;", "VarDecl", out _);
        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(parser.LastPartial);
    }
}
