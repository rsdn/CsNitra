#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// A5-1 (5b.4.1): bounded speculative probe — derivation of the predicate set (declaration only,
// not yet wired into S2 candidate generation). Hand-built snapshots, no parsing.
[TestClass]
public sealed class SpeculativeProbeDerivationTests
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

    // Regex-like catch-all terminal (one char, matches anywhere) — the "Identifier" of the spec.
    private sealed record Identifier(string Kind = "Identifier") : Terminal(Kind)
    {
        public override int TryMatch(string input, int startPos) => startPos < input.Length ? 1 : -1;
    }

    // Item := "i" "c" | "i" "d" — First = { "i" } (a single LITERAL — a valid probe).
    // Body := "[" Item* "]" — loop with a Ref body (T1 anchor source).
    // Outer := "a" Body* "b" — outer loop with Ref("Body") body.
    // Start := Ref("Outer") — a top rule that IS a Ref (alias).
    // TopDup := Ref("Body") — top rule that is a Ref, duplicating the inner loop body (dedup test).
    // Ident := Identifier Identifier | Identifier — First = { Identifier } (single catch-all
    //        REGEX — pure regex-First, must be excluded). One shared terminal instance: terminal
    //        identity for non-Literals is by reference (TerminalComparer).
    // IdentLoop := Ident* — loop whose Ref body has a pure regex-First.
    // Expr := Ref("Item") | Expr "+" Ref("Item") — TDOPP (self-recursive postfix) with a Ref prefix.
    // Ctx := Ref("Item") | ContextScope("c", Ref("Item")) — a rule containing a ContextScope.
    private static Parser NewParser()
    {
        var parser = new Parser(new SpaceTrivia("Trivia"));
        parser.MaxRecoveryIterations = 0;

        parser.Rules["Item"] = [
            new Seq([new Literal("i"), new Literal("c")], "ItemA"),
            new Seq([new Literal("i"), new Literal("d")], "ItemB"),
        ];
        parser.Rules["Body"] = [new Seq([new Literal("["), new ZeroOrMany(new Ref("Item")), new Literal("]")], "Body")];
        parser.Rules["Outer"] = [new Seq([new Literal("a"), new ZeroOrMany(new Ref("Body")), new Literal("b")], "Outer")];
        parser.Rules["Start"] = [new Ref("Outer")];
        parser.Rules["TopDup"] = [new Ref("Body")];

        var ident = new Identifier();
        parser.Rules["Ident"] = [
            new Seq([ident, ident], "Ident2"),
            ident,
        ];
        parser.Rules["IdentLoop"] = [new ZeroOrMany(new Ref("Ident"))];

        parser.Rules["Expr"] = [
            new Ref("Item"),
            new Seq([new Ref("Expr"), new Literal("+"), new Ref("Item")], "ExprPlus"),
        ];
        parser.Rules["Ctx"] = [
            new Ref("Item"),
            new ContextScope(new Literal("c"), new Ref("Item")),
        ];

        parser.BuildTdoppRules();
        return parser;
    }

    private static string[] Derive(Parser parser, StackFrame[] stack)
    {
        var snapshot = new FailureSnapshot(0, stack, new Literal("i"), [new Literal("i")]);
        return RecoveryEngine.DeriveProbePredicates(parser, snapshot).Select(r => r.RuleName).ToArray();
    }

    // (1) A loop-body Ref is included.
    [TestMethod]
    public void Test_LoopBodyRef_Is_Included()
    {
        var parser = NewParser();
        StackFrame[] stack = [
            new StackFrame("Body", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("Body", 0, new LoopFrameLocation("ZeroOrMany", 0), null, null),
        ];

        CollectionAssert.AreEqual(new[] { "Item" }, Derive(parser, stack));
    }

    // (2) The top-frame rule that is a Ref is included.
    [TestMethod]
    public void Test_TopFrameRef_Is_Included()
    {
        var parser = NewParser();
        StackFrame[] stack = [
            new StackFrame("Outer", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("Start", 0, new RuleFrameLocation(0), null, null),
        ];

        CollectionAssert.AreEqual(new[] { "Outer" }, Derive(parser, stack));
    }

    // (3) A pure regex-First rule (Identifier-like) is EXCLUDED — covered by S3/anchor-First.
    [TestMethod]
    public void Test_PureRegexFirst_Is_Excluded()
    {
        var parser = NewParser();
        StackFrame[] stack = [
            new StackFrame("IdentLoop", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("IdentLoop", 0, new LoopFrameLocation("ZeroOrMany", 0), null, null),
        ];

        CollectionAssert.AreEqual(Array.Empty<string>(), Derive(parser, stack));
    }

    // (4a) A TDOPP frame (PostfixFrameLocation) is EXCLUDED. Contrast: a RuleFrame of the same
    // TDOPP rule is NOT a TDOPP frame (A5-6 boundary) — its Ref prefix is derived.
    [TestMethod]
    public void Test_TdoppFrame_Is_Excluded()
    {
        var parser = NewParser();

        StackFrame[] postfixStack = [
            new StackFrame("Expr", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("Expr", 0, new PostfixFrameLocation(0), null, null),
        ];
        CollectionAssert.AreEqual(Array.Empty<string>(), Derive(parser, postfixStack));

        StackFrame[] ruleFrameStack = [
            new StackFrame("Expr", 0, new RuleFrameLocation(0), null, null),
        ];
        CollectionAssert.AreEqual(new[] { "Item" }, Derive(parser, ruleFrameStack));
    }

    // (4b) A ContextScope frame is EXCLUDED. The body frame ParseContextScope pushes is
    // SeqFrameLocation(1) with the enclosing rule name ("Ctx"); the rule definition is the marker.
    [TestMethod]
    public void Test_ContextScopeFrame_Is_Excluded()
    {
        var parser = NewParser();
        StackFrame[] stack = [
            new StackFrame("Ctx", 0, new RuleFrameLocation(1), null, null),
            new StackFrame("Ctx", 0, new SeqFrameLocation(1), null, null),
        ];

        CollectionAssert.AreEqual(Array.Empty<string>(), Derive(parser, stack));
    }

    // (5) Dedup by rule name + ordering: internal/top first, then loop frames top-to-bottom.
    // Top frame TopDup := Ref("Body") gives Ref("Body") first; the Outer loop's Ref("Body") is
    // deduped; the Body loop's Ref("Item") comes second.
    [TestMethod]
    public void Test_Dedup_And_TopFirstOrdering()
    {
        var parser = NewParser();
        StackFrame[] stack = [
            new StackFrame("Body", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("Body", 0, new LoopFrameLocation("ZeroOrMany", 0), null, null),
            new StackFrame("Outer", 0, new LoopFrameLocation("ZeroOrMany", 1), null, null),
            new StackFrame("TopDup", 0, new RuleFrameLocation(0), null, null),
        ];

        CollectionAssert.AreEqual(new[] { "Body", "Item" }, Derive(parser, stack));
    }

    // A5-1: accept depth K default is 2.
    [TestMethod]
    public void Test_SoftDepth_Default_Is_2()
    {
        Assert.AreEqual(2, new RecoveryOptions().SoftDepth);
        Assert.AreEqual(5, new RecoveryOptions { SoftDepth = 5 }.SoftDepth);
    }
}
