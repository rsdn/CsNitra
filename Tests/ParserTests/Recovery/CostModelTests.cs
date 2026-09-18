#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
namespace Recovery;

[TestClass]
public sealed class CostModelTests
{
    // === SkipCost ===

    [TestMethod]
    public void Test_SkipCost_Empty_Region_Is_Zero()
    {
        Assert.AreEqual(0, CostCalculator.SkipCost("abc", 2, 2));
        Assert.AreEqual(0, CostCalculator.SkipCost("abc", 0, 0));
    }

    [TestMethod]
    public void Test_SkipCost_No_Newlines_Is_WordCount()
    {
        var input = "hello world foo";
        Assert.AreEqual(3, CostCalculator.SkipCost(input, 0, input.Length));
        Assert.AreEqual(1, CostCalculator.SkipCost("x", 0, 1));
    }

    [TestMethod]
    public void Test_SkipCost_With_Newlines_Is_Words_Plus_Newlines()
    {
        var input = "a\nb c\nd";
        // слова: a, b, c, d = 4; переносы: 2 → 6
        Assert.AreEqual(6, CostCalculator.SkipCost(input, 0, input.Length));
    }

    [TestMethod]
    public void Test_SkipCost_Whitespace_Not_A_Word()
    {
        Assert.AreEqual(0, CostCalculator.SkipCost("   ", 0, 3));
        Assert.AreEqual(0, CostCalculator.SkipCost(" \t ", 0, 3));
        // только перенос строки без слов
        Assert.AreEqual(1, CostCalculator.SkipCost("  \n  ", 0, 5));
    }

    // === InsertCost ===

    [TestMethod]
    public void Test_InsertCost_Is_One()
    {
        Assert.AreEqual(1, CostCalculator.InsertCost);
    }

    // === TierPenalty ===

    [TestMethod]
    public void Test_TierPenalty_T1_Is_Zero()
    {
        Assert.AreEqual(0, CostCalculator.TierPenalty("T1"));
    }

    [TestMethod]
    public void Test_TierPenalty_T2_Is_One()
    {
        Assert.AreEqual(1, CostCalculator.TierPenalty("T2"));
    }

    // === CountRecoveryNodes ===

    [TestMethod]
    public void Test_CountRecoveryNodes_No_Recovery_Is_Zero()
    {
        var normal1 = new TerminalNode("A", 0, 1, 1);
        var normal2 = new TerminalNode("B", 1, 2, 1);
        var seq = new SeqNode("Seq", [normal1, normal2], 0, 2);
        Assert.AreEqual(0, CostCalculator.CountRecoveryNodes(seq));
    }

    [TestMethod]
    public void Test_CountRecoveryNodes_Counts_N_Recovery_Nodes()
    {
        var normal = new TerminalNode("A", 0, 1, 1);
        var rec1 = new TerminalNode("B", 1, 1, 0, IsRecovery: true);
        var rec2 = new TerminalNode("C", 2, 3, 1, IsRecovery: true);
        var seq = new SeqNode("Seq", [normal, rec1, rec2], 0, 3);
        Assert.AreEqual(2, CostCalculator.CountRecoveryNodes(seq));
    }

    [TestMethod]
    public void Test_CountRecoveryNodes_Nested_In_Some_And_List()
    {
        // SomeNode → recovery-узел
        var rec = new TerminalNode("X", 0, 1, 1, IsRecovery: true);
        var some = new SomeNode("Opt", rec, 0, 1);
        // ListNode → 2 recovery-элемента + 1 обычный разделитель
        var el1 = new TerminalNode("A", 0, 1, 1, IsRecovery: true);
        var delim = new TerminalNode(",", 1, 2, 1);
        var el2 = new TerminalNode("B", 2, 3, 1, IsRecovery: true);
        var list = new ListNode("List", [el1, el2], [delim], 0, 3);
        var seq = new SeqNode("Seq", [some, list], 0, 3);
        // recovery: X, A, B = 3
        Assert.AreEqual(3, CostCalculator.CountRecoveryNodes(seq));
    }

    // === Интеграция: cost реального кандидата соответствует единой формуле ===

    [TestMethod]
    public void Test_S5_Cost_Matches_Formula()
    {
        var parser = NewTrailingParser();
        var input = "abQ";
        parser.Parse(input, "Start", out _);
        var e = 2;
        var frame = new StackFrame("Start", 0, new RuleFrameLocation(0), [EofTerminal.Instance], null);
        var snapshot = new FailureSnapshot(e, [frame], new Literal("a"), [EofTerminal.Instance]);

        var candidates = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Success, "Start", 0, e);
        var c = candidates.Single(x => x.Rank == 5);

        Assert.AreEqual(CostCalculator.SkipCost(input, e, input.Length) + 1, c.Cost);
        Assert.AreEqual(2, c.Cost);
    }

    private static Parser NewTrailingParser()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));
        parser.MaxRecoveryIterations = 0;
        parser.Rules["Start"] = [new Seq([new Literal("a"), new Literal("b")], "Start")];
        parser.BuildTdoppRules();
        return parser;
    }
}
#endif
