#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class T2CollectionCapTests
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

    // Start = "a" Body "b", Body = "{" Loop "}", Loop = Item*, Item = "i" "c", ItemStart = "i".
    // Author CanStart = [ItemStart] on the Loop rule (NOT in any Anchors) — the live T2 path.
    // RecoveryRule as the rule ALTERNATIVE: the only shape whose options reach a snapshot frame
    // (rule frame, Parser.cs:328-329); a Seq-element RecoveryRule drops them (research 5b.4.4r3).
    private static Parser NewParser()
    {
        var parser = new Parser(new SpaceTrivia("Trivia"));
        parser.MaxRecoveryIterations = 0;
        parser.Rules["Start"] = [new Seq([new Literal("a"), new Ref("Body"), new Literal("b")], "Start")];
        parser.Rules["Body"] = [new Seq([new Literal("{"), new Ref("Loop"), new Literal("}")], "Body")];
        parser.Rules["Loop"] = [new RecoveryRule(new ZeroOrMany(new Ref("Item")), new RecoveryOptions { CanStart = [new Ref("ItemStart")] })];
        parser.Rules["Item"] = [new Seq([new Literal("i"), new Literal("c")], "Item")];
        parser.Rules["ItemStart"] = [new Seq([new Literal("i")], "ItemStart")];
        parser.BuildTdoppRules();
        return parser;
    }

    [TestMethod]
    public void Test_T2_Cap_Suppresses_Third_Accepted_Position()
    {
        var input = "a { i x i y i z i w } b";
        var parser = NewParser();
        parser.Parse(input, "Start", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;
        Assert.AreEqual(6, e);
        var candidates = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure, "Start", 0, e);
        var s2 = candidates.Where(c => c.Rank == 2).ToList();
        // ItemStart is accepted at 8, 12 and 16 (each a lone 'i' before a non-'c'); the cap
        // (K = 2) keeps the first two and suppresses the third.
        Assert.AreEqual(2, s2.Count);
        Assert.AreEqual(8, s2[0].Pos);
        Assert.AreEqual(12, s2[1].Pos);
        Assert.IsTrue(s2.All(c => c.TerminalKind == "ItemStart"));
        Assert.IsFalse(candidates.Any(c => c.Pos == 16), "the 3rd accepted position must be suppressed by the T2 cap");
    }

    [TestMethod]
    public void Test_T2_Cap_Allows_UpTo_K_Positions()
    {
        var input = "a { x i y i z } b";
        var parser = NewParser();
        parser.Parse(input, "Start", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;
        Assert.AreEqual(4, e);
        var candidates = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure, "Start", 0, e);
        var s2 = candidates.Where(c => c.Rank == 2).ToList();
        // Exactly K = 2 accepted positions — the cap does not suppress anything.
        Assert.AreEqual(2, s2.Count);
        Assert.AreEqual(6, s2[0].Pos);
        Assert.AreEqual(10, s2[1].Pos);
        Assert.IsTrue(s2.All(c => c.TerminalKind == "ItemStart"));
    }
}
