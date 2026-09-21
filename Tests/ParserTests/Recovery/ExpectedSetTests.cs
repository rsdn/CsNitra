#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// A4-7 (7.5.1): BuildExpectedSet = Expected верхнего кадра ∪ GetTerminators(стек) ∪ First суффиксных
// обязательств. Проверка, что объединённый набор — это ровно объединение трёх источников, и что каждый
// источник вносит свой терминал (топ-кадр Expected, терминатор, суффиксный First).
[TestClass]
public sealed class ExpectedSetTests
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

    private static Parser NewParser()
    {
        var parser = new Parser(new SpaceTrivia("Trivia"));
        parser.MaxRecoveryIterations = 0;
        return parser;
    }

    // Start := "a" Body "z"; Body := "b" Mid "c"; Mid := Seq("m").
    // Падение в Mid (совпадение "m" в EOF после "ab"): топ-кадр Mid Seq(0), Expected = {m}.
    // Источники (по трассировке per-site/суффиксов):
    //   1. Expected верхнего кадра  = {m}          — «m» уникален для источника 1;
    //   2. GetTerminators(стек)     = {c, EOF}     — «EOF» уникален для источника 2;
    //   3. First суффикс. обязат.   = {c, z}       — «z» уникален для источника 3.
    // Объединение = {m, c, z, EOF}.
    private static (Parser Parser, FailureSnapshot Snapshot) BuildFailingParse()
    {
        var parser = NewParser();
        parser.Rules["Start"] = [new Seq([new Literal("a"), new Ref("Body"), new Literal("z")], "Start")];
        parser.Rules["Body"] = [new Seq([new Literal("b"), new Ref("Mid"), new Literal("c")], "Body")];
        parser.Rules["Mid"] = [new Seq([new Literal("m")], "Mid")];
        parser.BuildTdoppRules();

        parser.Parse("ab", "Start", out _);
        var snap = parser.LastSnapshot;
        if (snap is null)
            throw new InvalidOperationException("expected a failure snapshot");
        return (parser, snap);
    }

    [TestMethod]
    public void Test_Setup_TopFrame_Is_MidSeq0()
    {
        var (_, snap) = BuildFailingParse();
        var top = snap.Stack[^1];
        Assert.AreEqual("Mid", top.RuleName);
        Assert.IsTrue(top.Location is SeqFrameLocation { ElementIndex: 0 });
        Assert.IsTrue((top.Expected ?? []).Any(t => t.Kind == "m"), "top frame must expect 'm'");
    }

    [TestMethod]
    public void Test_BuildExpectedSet_Is_UnionOfThreeSources()
    {
        var (parser, snap) = BuildFailingParse();

        var actualKinds = new HashSet<string>(RecoveryEngine.BuildExpectedSet(snap, parser).Select(t => t.Kind), StringComparer.Ordinal);

        // Три источника, вычисленные независимо:
        var top = snap.Stack[^1];
        var source1 = new HashSet<string>((top.Expected ?? []).Select(t => t.Kind), StringComparer.Ordinal);
        var source2 = new HashSet<string>(parser.GetTerminators(snap.Stack).Select(t => t.Kind), StringComparer.Ordinal);
        var source3 = new HashSet<string>(
            RecoveryEngine.GetSuffixObligationFirsts(snap, parser, snap.Stack.Length - 1).Select(x => x.T.Kind),
            StringComparer.Ordinal);

        var union = new HashSet<string>(source1.Concat(source2).Concat(source3), StringComparer.Ordinal);
        CollectionAssert.AreEquivalent(union.ToList(), actualKinds.ToList());
    }

    [TestMethod]
    public void Test_BuildExpectedSet_EachSourceContributes()
    {
        var (parser, snap) = BuildFailingParse();
        var actualKinds = new HashSet<string>(RecoveryEngine.BuildExpectedSet(snap, parser).Select(t => t.Kind), StringComparer.Ordinal);
        var terminators = parser.GetTerminators(snap.Stack);
        var suffixFirsts = RecoveryEngine.GetSuffixObligationFirsts(snap, parser, snap.Stack.Length - 1);
        var topExpected = snap.Stack[^1].Expected ?? [];

        // Источник 1 (Expected верхнего кадра): «m» присутствует и отсутствует в источниках 2 и 3.
        Assert.IsTrue(actualKinds.Contains("m"), "combined set must contain top-frame Expected 'm'");
        Assert.IsFalse(terminators.Any(t => t.Kind == "m"), "'m' must not be a terminator");
        Assert.IsFalse(suffixFirsts.Any(x => x.T.Kind == "m"), "'m' must not be a suffix First");

        // Источник 3 (First суффиксных обязательств): «z» (хвост Start после Body) присутствует и
        // отсутствует в источниках 1 и 2.
        Assert.IsTrue(actualKinds.Contains("z"), "combined set must contain suffix First 'z'");
        Assert.IsFalse(topExpected.Any(t => t.Kind == "z"), "'z' must not be a top-frame Expected");
        Assert.IsFalse(terminators.Any(t => t.Kind == "z"), "'z' must not be a terminator");

        // Источник 2 (терминаторы): «EOF» присутствует и отсутствует в источниках 1 и 3.
        Assert.IsTrue(actualKinds.Contains("EOF"), "combined set must contain terminator EOF");
        Assert.IsTrue(terminators.Any(t => t.Kind == "EOF"), "terminators must contain EOF");
        Assert.IsFalse(topExpected.Any(t => t.Kind == "EOF"), "EOF must not be a top-frame Expected");
        Assert.IsFalse(suffixFirsts.Any(x => x.T.Kind == "EOF"), "suffix First must not contain EOF");
    }

    [TestMethod]
    public void Test_BuildExpectedSet_ExactKinds()
    {
        var (parser, snap) = BuildFailingParse();
        var actualKinds = RecoveryEngine.BuildExpectedSet(snap, parser).Select(t => t.Kind).ToArray();

        // Точный объединённый набор (order-independent): {m, c, z, EOF}.
        CollectionAssert.AreEquivalent(new[] { "EOF", "c", "m", "z" }, actualKinds);
    }
}
