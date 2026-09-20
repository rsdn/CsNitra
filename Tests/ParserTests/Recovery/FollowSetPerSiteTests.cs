using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public class FollowSetPerSiteTests
{
    // X := "x";  List := X "," X;  Block := X "}";
    // StartList := List;  StartBlock := Block;  StartY := Y "!";  Y := "a" X
    // follow(X) = { "," , "}" , "!" };  follow(List) = follow(Block) = follow(Start*) = { EOF }
    private static FollowSetCalculator BuildCalculator()
    {
        var rules = new Dictionary<string, Rule[]>
        {
            {"X", new Rule[] { new Literal("x") } },
            {"List", new Rule[] { new Seq(new Rule[] { new Ref("X"), new Literal(","), new Ref("X") }, "List") } },
            {"Block", new Rule[] { new Seq(new Rule[] { new Ref("X"), new Literal("}") }, "Block") } },
            {"StartList", new Rule[] { new Ref("List") } },
            {"StartBlock", new Rule[] { new Ref("Block") } },
            {"StartY", new Rule[] { new Seq(new Rule[] { new Ref("Y"), new Literal("!") }, "StartY") } },
            {"Y", new Rule[] { new Seq(new Rule[] { new Literal("a"), new Ref("X") }, "Y") } },
        };
        return new FollowSetCalculator(rules, "StartList", "StartBlock", "StartY");
    }

    private static bool ContainsKind(Terminal[] terminators, string kind)
    {
        foreach (var t in terminators)
            if (t.Kind == kind)
                return true;
        return false;
    }

    private static HashSet<string> NonEofKinds(Terminal[] terminators)
    {
        var kinds = new HashSet<string>();
        foreach (var t in terminators)
            if (t is not EofTerminal)
                kinds.Add(t.Kind);
        return kinds;
    }

    // ============ Per-site narrowness ============
    [TestMethod]
    public void Test_List_ContainsComma_NotBrace()
    {
        var calc = BuildCalculator();
        var stack = new List<StackFrame>
        {
            new StackFrame("StartList", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("StartList", 0, new SeqFrameLocation(0), null, null),
            new StackFrame("List", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("List", 0, new SeqFrameLocation(0), null, null),
            new StackFrame("X", 0, new RuleFrameLocation(0), null, null),
        };

        var terminators = calc.GetTerminatorsPerSite(stack);

        Assert.IsTrue(ContainsKind(terminators, ","), "X inside List: per-site terminators must contain ','");
        Assert.IsFalse(ContainsKind(terminators, "}"), "X inside List: per-site terminators must not contain '}'");
    }

    [TestMethod]
    public void Test_Block_ContainsBrace_NotComma()
    {
        var calc = BuildCalculator();
        var stack = new List<StackFrame>
        {
            new StackFrame("StartBlock", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("StartBlock", 0, new SeqFrameLocation(0), null, null),
            new StackFrame("Block", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("Block", 0, new SeqFrameLocation(0), null, null),
            new StackFrame("X", 0, new RuleFrameLocation(0), null, null),
        };

        var terminators = calc.GetTerminatorsPerSite(stack);

        Assert.IsTrue(ContainsKind(terminators, "}"), "X inside Block: per-site terminators must contain '}'");
        Assert.IsFalse(ContainsKind(terminators, ","), "X inside Block: per-site terminators must not contain ','");
    }

    [TestMethod]
    public void Test_PerSite_Narrower_Than_RuleLevel()
    {
        var calc = BuildCalculator();
        var follow = calc.GetFollowSet("X");

        Assert.IsTrue(follow.Contains(new Literal(","), TerminalComparer.Instance), "rule-level follow(X) must contain ','");
        Assert.IsTrue(follow.Contains(new Literal("}"), TerminalComparer.Instance), "rule-level follow(X) must contain '}'");
    }

    // ============ Empty tail passthrough (CASE 2) ============
    [TestMethod]
    public void Test_TailEmpty_Passthrough()
    {
        // Y := "a" X — X is the LAST element of Y: tail is empty, so the site must
        // inherit the outer follow ("!" from StartY := Y "!") instead of returning nothing.
        var calc = BuildCalculator();
        var stack = new List<StackFrame>
        {
            new StackFrame("StartY", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("StartY", 0, new SeqFrameLocation(0), null, null),
            new StackFrame("Y", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("Y", 0, new SeqFrameLocation(1), null, null),
            new StackFrame("X", 0, new RuleFrameLocation(0), null, null),
        };

        var terminators = calc.GetTerminatorsPerSite(stack);

        Assert.IsTrue(ContainsKind(terminators, "!"), "X as last element of Y must inherit the outer follow '!'");
    }

    // ============ RuleFrame-only fallback ============
    [TestMethod]
    public void Test_RuleFrameOnly_Equals_RuleLevel()
    {
        // RuleFrame-only stack: StartList := Ref("List") — the parser pushes RuleFrame(StartList)
        // then RuleFrame(List) (no SeqFrame, since StartList's production is a plain Ref).
        // follow(List) = follow(StartList) = { EOF }, so per-site and rule-level agree.
        var calc = BuildCalculator();
        var stack = new List<StackFrame>
        {
            new StackFrame("StartList", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("List", 0, new RuleFrameLocation(0), null, null),
        };

        var perSite = calc.GetTerminatorsPerSite(stack);
        var ruleLevel = calc.GetTerminators(stack);

        CollectionAssert.AreEquivalent(NonEofKinds(ruleLevel).ToList(), NonEofKinds(perSite).ToList());
        Assert.AreEqual("EOF", perSite[^1].Kind, "per-site result must end with EOF");
        Assert.AreEqual("EOF", ruleLevel[^1].Kind, "rule-level result must end with EOF");
    }

    // ============ Loop per-site: no over-include of enclosing rule follow ============
    [TestMethod]
    public void Test_LoopPerSite_NoOverInclude()
    {
        // Item := "i";  Body := "{" ZeroOrMany(Item) "}";  Start := "a" Body "b".
        // follow(Body) = { "b" } — the loop must NOT inherit it; loop terminators =
        // First(Item) ∪ First("}") = { "i", "}" }.
        var rules = new Dictionary<string, Rule[]>
        {
            {"Item", new Rule[] { new Literal("i") } },
            {"Body", new Rule[] { new Seq(new Rule[] { new Literal("{"), new ZeroOrMany(new Ref("Item")), new Literal("}") }, "Body") } },
            {"Start", new Rule[] { new Seq(new Rule[] { new Literal("a"), new Ref("Body"), new Literal("b") }, "Start") } },
        };
        var calc = new FollowSetCalculator(rules, "Start");
        var stack = new List<StackFrame>
        {
            new StackFrame("Start", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("Start", 0, new SeqFrameLocation(1), null, null),
            new StackFrame("Body", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("Body", 0, new SeqFrameLocation(1), null, null),
            new StackFrame("Body", 0, new LoopFrameLocation("ZeroOrMany", 0), null, null),
            new StackFrame("Item", 0, new RuleFrameLocation(0), null, null),
        };

        var terminators = calc.GetTerminatorsPerSite(stack);

        Assert.IsTrue(ContainsKind(terminators, "i"), "loop body first: per-site terminators must contain 'i'");
        Assert.IsTrue(ContainsKind(terminators, "}"), "loop tail first: per-site terminators must contain '}'");
        Assert.IsFalse(ContainsKind(terminators, "b"), "loop must not inherit follow(Body)='b'");
    }
}
