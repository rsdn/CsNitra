using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class RecoveryStructureTests
{
    [DataTestMethod]
    [DataRow("a; $ b;", "ab")]
    [DataRow("a $ b; c", "abc")]
    [DataRow("a; $$ b $$ c;", "abc")]
    public void LaterDeclarationsSurviveDamage(string input, string expected)
    {
        var parser = Create();
        var result = parser.Parse(input, "Module", out _);
        Assert.IsTrue(result.TryGetSuccess(out var tree, out var end));
        Assert.AreEqual(input.Length, end);
        var declarations = Walk(tree).OfType<SeqNode>().Where(n => n.Kind == "Declaration");
        var names = string.Concat(declarations.SelectMany(n => n.RawElements)
            .OfType<TerminalNode>().Where(n => !n.IsRecovery && n.ContentLength == 1)
            .Select(n => input[n.StartPos]).Where(c => c is 'a' or 'b' or 'c'));
        Assert.AreEqual(expected, names);
        Assert.IsTrue(parser.RecoveryDiagnostics.Count > 0);
        AssertCoverage(tree, input.Length);
    }

    [TestMethod]
    public void StrictFallbackRetainsSourceAndReportsUnrecovered()
    {
        var parser = Create();
        parser.Rules["Module"] = [new RecoveryRule(new Seq([new Literal("a"), new Literal(";")], "Strict"),
            new RecoveryOptions { Recoverable = false })];
        parser.BuildTdoppRules();
        var result = parser.Parse("a $$", "Module", out _);
        Assert.IsTrue(result.TryGetSuccess(out var tree, out var end) && end == 4);
        Assert.IsTrue(parser.RecoveryDiagnostics.Any(d => d.Kind == RecoveryKind.Unrecovered));
        AssertCoverage(tree, 4);
    }

    [TestMethod]
    public void NestedDamagePreservesAnOuterDeclaration()
    {
        var parser = Create();
        parser.Rules["Outer"] = [new Ref("Item"), new Seq([new Literal("{"),
            new ZeroOrMany(new Ref("Item"), "Inner"), new Literal("}")], "Block")];
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Outer"), "OuterItems")];
        parser.BuildTdoppRules();
        const string input = "{ a; $ b; } c;";
        Assert.IsTrue(parser.Parse(input, "Module", out _).TryGetSuccess(out var root, out var end));
        Assert.AreEqual(input.Length, end);
        AssertCoverage(root, input.Length);
        var outer = Walk(root).OfType<SeqNode>().Single(n => n.Kind == "Declaration" &&
            n.RawElements.OfType<TerminalNode>().Any(t => !t.IsRecovery && input[t.StartPos] == 'c'));
        Assert.IsFalse(outer.IsRecovery);
        Assert.AreEqual(3, Walk(root).OfType<SeqNode>().Count(n => n.Kind == "Declaration"));
        Assert.IsTrue(parser.RecoveryDiagnostics.Count > 0);
    }

    [TestMethod]
    public void DamagedSeparatedItemPreservesTheFollowingItem()
    {
        var parser = Create();
        parser.Rules["Module"] = [new SeparatedList(new Ref("Item"), new Literal(","), Kind: "Items",
            EndBehavior: SeparatorEndBehavior.Forbidden, CanBeEmpty: false)];
        parser.BuildTdoppRules();
        const string input = "a;, b $, c;";
        Assert.IsTrue(parser.Parse(input, "Module", out _).TryGetSuccess(out var root, out var end));
        Assert.AreEqual(input.Length, end);
        AssertCoverage(root, input.Length);
        Assert.AreEqual(1, Walk(root).OfType<SeqNode>().Count(n => n.Kind == "Declaration" &&
            n.RawElements.OfType<TerminalNode>().Any(t => !t.IsRecovery && input[t.StartPos] == 'c')));
        Assert.IsTrue(parser.RecoveryDiagnostics.Count > 0);
    }

    private static Parser Create()
    {
        var parser = new Parser(RecoveryTerminals.Trivia()) { Profile = RecoveryProfile.Test };
        parser.Rules["Item"] = new[] { "a", "b", "c" }.Select(name => (Rule)new Seq(
            [new Literal(name), new OftenMissed(new Literal(";"))], "Declaration")).ToArray();
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Item"), "Declarations")];
        parser.BuildTdoppRules();
        return parser;
    }

    internal static IEnumerable<ISyntaxNode> Walk(ISyntaxNode node)
    {
        yield return node;
        IEnumerable<ISyntaxNode> children = node switch
        {
            SeqNode seq => seq.RawElements,
            ListNode list => list.RawElements.Concat(list.Delimiters).OrderBy(n => n.StartPos),
            SomeNode some => new[] { some.Value },
            _ => Array.Empty<ISyntaxNode>()
        };
        foreach (var child in children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    internal static void AssertCoverage(ISyntaxNode tree, int length)
    {
        var cursor = 0;
        foreach (var terminal in Walk(tree).OfType<TerminalNode>().Where(n => n.EndPos > n.StartPos)
            .OrderBy(n => n.StartPos))
        {
            Assert.AreEqual(cursor, terminal.StartPos, "Source must have neither gaps nor overlapping leaves.");
            cursor = terminal.EndPos;
        }
        Assert.AreEqual(length, cursor);
    }
}
