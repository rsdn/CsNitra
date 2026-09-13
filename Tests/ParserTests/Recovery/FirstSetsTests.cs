#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
namespace Recovery;

[TestClass]
public sealed class FirstSetsTests
{
    private static void AssertKinds(Terminal[] terminals, params string[] expected)
        => CollectionAssert.AreEqual(expected, terminals.Select(t => t.Kind).ToArray());

    // ============ Terminal ============

    [TestMethod]
    public void Test_Terminal()
    {
        AssertKinds(FirstSets.Get(new Literal("a")), "a");
        Assert.IsFalse(FirstSets.IsNullable(new Literal("a")));
    }

    [TestMethod]
    public void Test_EmptyTerminal_IsRegularTerminal()
    {
        var empty = new EmptyTerminal("Error");
        var first = FirstSets.Get(empty);
        Assert.AreEqual(1, first.Length);
        Assert.IsTrue(ReferenceEquals(first[0], empty));
        Assert.IsFalse(FirstSets.IsNullable(empty));
    }

    // ============ Seq ============

    [TestMethod]
    public void Test_Seq_FirstElementOnly()
    {
        var rule = new Seq([new Literal("a"), new Literal("b")], "S");
        AssertKinds(FirstSets.Get(rule), "a");
    }

    [TestMethod]
    public void Test_Seq_NullableFallthrough()
    {
        var rule = new Seq([new Optional(new Literal("a")), new Literal("b")], "S");
        AssertKinds(FirstSets.Get(rule), "a", "b");
    }

    [TestMethod]
    public void Test_Seq_DeepNullableFallthrough()
    {
        // Seq(Optional(Seq(Optional(x), y)), z) -> [x, y, z]
        var rule = new Seq([
            new Optional(new Seq([new Optional(new Literal("x")), new Literal("y")], "Inner")),
            new Literal("z"),
        ], "S");
        AssertKinds(FirstSets.Get(rule), "x", "y", "z");
    }

    [TestMethod]
    public void Test_Seq_StopsAtNonNullable()
    {
        var rule = new Seq([new Literal("a"), new Optional(new Literal("b")), new Literal("c")], "S");
        AssertKinds(FirstSets.Get(rule), "a");
    }

    [TestMethod]
    public void Test_Seq_Dedup()
    {
        var rule = new Seq([new Literal("a"), new Literal("a")], "S");
        var first = FirstSets.Get(rule);
        Assert.AreEqual(1, first.Length);
        Assert.AreEqual("a", first[0].Kind);
    }

    // ============ Loops / Optional ============

    [TestMethod]
    public void Test_OneOrMany()
    {
        var rule = new OneOrMany(new Literal("a"));
        AssertKinds(FirstSets.Get(rule), "a");
        Assert.IsFalse(FirstSets.IsNullable(rule));
    }

    [TestMethod]
    public void Test_ZeroOrMany()
    {
        var rule = new ZeroOrMany(new Literal("a"));
        AssertKinds(FirstSets.Get(rule), "a");
        Assert.IsTrue(FirstSets.IsNullable(rule));
    }

    [TestMethod]
    public void Test_Optional()
    {
        var rule = new Optional(new Literal("a"));
        AssertKinds(FirstSets.Get(rule), "a");
        Assert.IsTrue(FirstSets.IsNullable(rule));
    }

    [TestMethod]
    public void Test_OftenMissed()
    {
        var rule = new OftenMissed(new Literal("a"));
        AssertKinds(FirstSets.Get(rule), "a");
        Assert.IsTrue(FirstSets.IsNullable(rule));
    }

    // ============ Predicates ============

    [TestMethod]
    public void Test_AndPredicate()
    {
        var rule = new AndPredicate(new Literal("a"));
        Assert.AreEqual(0, FirstSets.Get(rule).Length);
        Assert.IsTrue(FirstSets.IsNullable(rule));
    }

    [TestMethod]
    public void Test_NotPredicate()
    {
        var rule = new NotPredicate(new Literal("a"));
        Assert.AreEqual(0, FirstSets.Get(rule).Length);
        Assert.IsTrue(FirstSets.IsNullable(rule));
    }

    // ============ SeparatedList ============

    [TestMethod]
    public void Test_SeparatedList_CanBeEmpty()
    {
        var rule = new SeparatedList(new Literal("a"), new Literal(","), "L", CanBeEmpty: true);
        Assert.AreEqual(0, FirstSets.Get(rule).Length);
        Assert.IsTrue(FirstSets.IsNullable(rule));
    }

    [TestMethod]
    public void Test_SeparatedList_NotEmpty()
    {
        var rule = new SeparatedList(new Literal("a"), new Literal(","), "L", CanBeEmpty: false);
        AssertKinds(FirstSets.Get(rule), "a");
        Assert.IsFalse(FirstSets.IsNullable(rule));
    }

    // ============ Ref ============

    [TestMethod]
    public void Test_Ref_WithCalculator()
    {
        var rules = new Dictionary<string, Rule[]>
        {
            ["Expr"] = [new Seq([new Literal("a"), new Literal("b")], "Expr")],
            ["Start"] = [new Ref("Expr")],
        };
        var calc = new FollowSetCalculator(rules, "Start");

        AssertKinds(FirstSets.Get(new Ref("Expr"), calc), "a");
        Assert.IsFalse(FirstSets.IsNullable(new Ref("Expr"), calc));
    }

    [TestMethod]
    public void Test_Ref_WithoutCalculator()
    {
        Assert.AreEqual(0, FirstSets.Get(new Ref("Expr")).Length);
        Assert.IsFalse(FirstSets.IsNullable(new Ref("Expr")));
    }

    [TestMethod]
    public void Test_Ref_Nullable_FiltersEpsilon()
    {
        var rules = new Dictionary<string, Rule[]>
        {
            ["Opt"] = [new Optional(new Literal("x"))],
            ["Start"] = [new Ref("Opt")],
        };
        var calc = new FollowSetCalculator(rules, "Start");

        // ε from the nullable rule must not be represented
        AssertKinds(FirstSets.Get(new Ref("Opt"), calc), "x");
        Assert.IsTrue(FirstSets.IsNullable(new Ref("Opt"), calc));
    }

    // ============ TdoppRule ============

    [TestMethod]
    public void Test_TdoppRule()
    {
        var tdopp = new TdoppRule(
            new Ref("Expr"),
            "Expr",
            Prefix: [new Literal("a"), new Optional(new Literal("b"))],
            Postfix: [],
            RecoveryPrefix: [],
            RecoveryPostfix: []);

        AssertKinds(FirstSets.Get(tdopp), "a", "b");
        Assert.IsTrue(FirstSets.IsNullable(tdopp));
    }

    // ============ IsNullable variants ============

    [TestMethod]
    public void Test_IsNullable_Variants()
    {
        Assert.IsFalse(FirstSets.IsNullable(new Literal("a")));
        Assert.IsFalse(FirstSets.IsNullable(new Seq([new Literal("a"), new Literal("b")], "S")));
        Assert.IsTrue(FirstSets.IsNullable(new Seq([new Optional(new Literal("a"))], "S")));
        Assert.IsFalse(FirstSets.IsNullable(new OneOrMany(new Literal("a"))));
        Assert.IsTrue(FirstSets.IsNullable(new ZeroOrMany(new Literal("a"))));
        Assert.IsTrue(FirstSets.IsNullable(new AndPredicate(new Literal("a"))));
        Assert.IsFalse(FirstSets.IsNullable(new SeparatedList(new Literal("a"), new Literal(","), "L", CanBeEmpty: false)));
        Assert.IsTrue(FirstSets.IsNullable(new SeparatedList(new Literal("a"), new Literal(","), "L", CanBeEmpty: true)));
    }
}

#endif
