using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class S1PerSiteTests
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

    // X := "x";  List := X "," X;  Block := X "}";  Start := List
    // rule-level follow(X) = { "," , "}" };  per-site in the List context = { "," }.
    // Block is unreferenced by the start rule but still contributes '}' to rule-level follow(X).
    private static (Parser Parser, Literal Comma) NewParser()
    {
        var parser = new Parser(new SpaceTrivia("Trivia"));
        parser.MaxRecoveryIterations = 0;
        var comma = new Literal(",");
        var brace = new Literal("}");
        parser.Rules["X"] = [new Literal("x")];
        parser.Rules["List"] = [new Seq([new Ref("X"), comma, new Ref("X")], "List")];
        parser.Rules["Block"] = [new Seq([new Ref("X"), brace], "Block")];
        parser.Rules["Start"] = [new Ref("List")];
        parser.BuildTdoppRules();
        return (parser, comma);
    }

    [TestMethod]
    public void Test_S1_List_ContainsComma_NotBrace()
    {
        var (parser, comma) = NewParser();
        var stack = new StackFrame[]
        {
            new StackFrame("Start", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("Start", 0, new SeqFrameLocation(0), null, null),
            new StackFrame("List", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("List", 0, new SeqFrameLocation(0), null, null),
            new StackFrame("X", 0, new RuleFrameLocation(0), null, null),
        };
        var snapshot = new FailureSnapshot(1, stack, comma, [comma]);

        var candidates = RecoveryEngine.Generate(1, snapshot, "xx", parser, Result.Kind.Failure, "Start", 0, 1);

        var s1 = candidates.Where(x => x.Id.StartsWith("S1:")).ToList();
        Assert.IsTrue(s1.Any(x => x.TerminalKind == ","), "S1 must contain the per-site ',' candidate");
        Assert.IsFalse(s1.Any(x => x.TerminalKind == "}"), "S1 must not contain the rule-level-only '}' candidate");
    }
}
