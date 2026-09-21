#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class R2ExpectedStopTests
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

    // R2 (5b.5): Module = Ref(S), S = "a" "b" "c". Input "a $ x c": падение на "b" @2 ($),
    // терминаторы = {EOF}, суффиксные обязательства = {Literal("c")} → стоп-точка 6, без R2 — 7 (EOF).
    private static Parser NewParser()
    {
        var parser = new Parser(new SpaceTrivia("Trivia"));
        parser.Rules["Module"] = [new Ref("S")];
        parser.Rules["S"] = [new Seq([new Literal("a"), new Literal("b"), new Literal("c")], "S")];
        parser.BuildTdoppRules();
        return parser;
    }

    [TestMethod]
    public void Test_S3_Stops_At_SuffixObligation_First()
    {
        var input = "a $ x c";
        var parser = NewParser();
        parser.MaxRecoveryIterations = 0;
        parser.Parse(input, "Module", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;
        Assert.AreEqual(2, e);
        // parseEnd = 0 < input.Length → S6 тоже генерируется (тест 2).
        var candidates = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure, "Module", 0, 0);
        var s3 = candidates.Where(c => c.Rank == 3).ToList();
        Assert.AreEqual(1, s3.Count);
        // R2: стоп на First суффиксного обязательства ("c" @6); без R2 — EOF @7.
        Assert.AreEqual(6, s3[0].Pos);
    }

    [TestMethod]
    public void Test_S6_Stops_At_SuffixObligation_First()
    {
        var input = "a $ x c";
        var parser = NewParser();
        parser.MaxRecoveryIterations = 0;
        parser.Parse(input, "Module", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;
        Assert.AreEqual(2, e);
        var candidates = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure, "Module", 0, 0);
        var s6 = candidates.Where(c => c.Rank == 6).ToList();
        Assert.AreEqual(1, s6.Count);
        // R2: дно останавливается на "c" @6; без R2 — EOF @7.
        Assert.AreEqual(6, s6[0].Pos);
    }

    [TestMethod]
    public void Test_E2E_Resync_At_SuffixObligation_Start()
    {
        var input = "a $ x c";
        var parser = NewParser();
        var result = parser.Parse(input, "Module", out _);
        // R2: resync в старт ожидаемого продолжения ("c" @6), а не на EOF @7.
        Assert.IsTrue(result.TryGetSuccess(out var tree, out var end) && end == input.Length, "E2E: not recovered to EOF");
        Assert.IsNull(parser.ErrorInfo);
        Assert.AreEqual(6, FirstAbsorberEndPos(tree), "E2E: absorber must end at 6 (start of the expected continuation), not 7 (EOF)");
    }

    // Метрика resync (волна 5): EndPos ПЕРВОГО IsAbsorber-узла в дереве (документный порядок) —
    // полный span пропущенного региона = точка, куда recovery пришёл. Display-диагностик (A5-3)
    // показывает только первое слово региона и для метрики не годится (D1.7: [7,10) вместо [7,11)).
    private static int? FirstAbsorberEndPos(ISyntaxNode? node)
    {
        if (node is null)
            return null;
        if (node is TerminalNode { IsAbsorber: true } absorber)
            return absorber.EndPos;
        foreach (var child in Children(node))
        {
            var found = FirstAbsorberEndPos(child);
            if (found is { } f)
                return f;
        }
        return null;

        // Дети (как в S3TriviaJumpTests.AbsorberEnds): SeqNode/ListNode — RawElements
        // (+ListNode.Delimiters), SomeNode — Value, TerminalNode — лист.
        static IEnumerable<ISyntaxNode> Children(ISyntaxNode n)
        {
            switch (n)
            {
                case SeqNode s:
                    return s.RawElements;
                case ListNode l:
                {
                    var children = new List<ISyntaxNode>(l.RawElements);
                    children.AddRange(l.Delimiters);
                    return children;
                }
                case SomeNode so:
                    return new ISyntaxNode[] { so.Value };
                default:
                    return Array.Empty<ISyntaxNode>();
            }
        }
    }
}
