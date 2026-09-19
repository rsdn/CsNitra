#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
namespace Recovery;

[TerminalMatcher]
public sealed partial class S6Terminals
{
    [Regex(@"\d+")]
    public static partial Terminal Number();

    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

[TestClass]
public sealed class S6BottomTests
{
    // ����� ������: ��� IsAbsorber-���� (���������).
    private static IEnumerable<TerminalNode> FindAbsorbers(ISyntaxNode node)
    {
        switch (node)
        {
            case TerminalNode t when t.IsAbsorber:
                yield return t;
                break;
            case SeqNode seq:
                foreach (var el in seq.RawElements)
                    foreach (var a in FindAbsorbers(el))
                        yield return a;
                break;
            case ListNode list:
                foreach (var el in list.RawElements)
                    foreach (var a in FindAbsorbers(el))
                        yield return a;
                foreach (var d in list.Delimiters)
                    foreach (var a in FindAbsorbers(d))
                        yield return a;
                break;
            case SomeNode some:
                foreach (var a in FindAbsorbers(some.Value))
                    yield return a;
                break;
        }
    }

    // ============ A1 ������: ������������� Seq-����� (snapshot == null) ============
    // Module := Expr Expr, Expr := Number (\d+), ���� "12 34 56 78" > ������ Success@6 ��� mismatch
    // (snapshot == null). �������� � (6, firstTerminal) �� �������� ������������� Seq > S6 memo-������
    // start-������� �� currentStartPos > Success@EOF, ����� [6..EOF) ������ IsAbsorber-�����.
    [TestMethod]
    public void Test_S6_FixedSeq_StartRule_TailCoveredByAbsorber()
    {
        var parser = new Parser(S6Terminals.Trivia());
        parser.Rules["Expr"] = [S6Terminals.Number()];
        parser.Rules["Module"] = [new Seq([new Ref("Expr"), new Ref("Expr")], "Module")];
        parser.BuildTdoppRules();

        var input = "12 34 56 78";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end), $"expected Success, got {result.ResultKind}");
        Assert.AreEqual(input.Length, end, "S6 bottom must reach EOF");
        Assert.IsNull(parser.ErrorInfo);

        // ����� [6..EOF) ������ ���������� (�������� ������� � ��� Number [0..6)).
        var prefixEnd = 6;
        var absorber = FindAbsorbers(node).Single();
        Assert.AreEqual(prefixEnd, absorber.StartPos);
        Assert.AreEqual(input.Length, absorber.EndPos);
        Assert.IsTrue(absorber.IsRecovery);
    }

    // ============ ����������� ����� (snapshot != null): ��������� ����� ����� ����� ============
    // Module := ZeroOrMany(Number), ����� "###" ����� ��������� ����� > Success@EOF, ����� ������ ����������.
    [TestMethod]
    public void Test_S6_LoopBased_StartRule_TrailingGarbage()
    {
        var parser = new Parser(S6Terminals.Trivia());
        parser.Rules["Module"] = [new ZeroOrMany(S6Terminals.Number(), "Numbers")];
        parser.BuildTdoppRules();

        var input = "12 34 56 ###";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end), $"expected Success, got {result.ResultKind}");
        Assert.AreEqual(input.Length, end, "loop-based start rule must reach EOF");
        Assert.IsNull(parser.ErrorInfo);
        Assert.IsTrue(FindAbsorbers(node).Any(), "trailing garbage must be covered by an absorber");
    }

    // ============ ������� ������ (Recoverable:false): S6 �� ������������ (C1) ============
    // ������ � ������ Recoverable=false > Generate ���������� ������ ������ (�� S1..S4, �� S5, �� S6).
    [TestMethod]
    public void Test_S6_Only_Generated_In_Strict_Region()
    {
        var parser = new Parser(S6Terminals.Trivia());
        var strictLiteral = new RecoveryRule(new Literal("42"), new RecoveryOptions { Recoverable = false });
        parser.Rules["Expr"] = new Rule[] { strictLiteral };
        parser.Rules["Module"] = [new Seq([new Literal("a"), new Ref("Expr")], "Mod")];
        parser.BuildTdoppRules();

        var input = "abc";
        parser.Parse(input, "Module", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;
        Assert.IsTrue(snapshot.Stack.Any(f => f.Options is { Recoverable: false }), "expected a strict frame in the snapshot");

        // strict + parseEnd < EOF: S1..S5 suppressed, exactly one candidate — S6 (C1).
        var candidates = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure, "Module", 0, e);
        Assert.IsTrue(
            candidates.All(c => c.Rank == 6),
            $"strict region must yield only S6, got ranks [{string.Join(",", candidates.Select(c => c.Rank))}]");
        Assert.AreEqual(1, candidates.Count, "strict region with parseEnd < EOF must yield exactly one candidate (S6)");

        // strict + parseEnd == EOF: no trailing region to absorb, so no candidates at all.
        var atEof = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure, "Module", 0, input.Length);
        Assert.AreEqual(0, atEof.Count, "strict region at EOF must yield no candidates");
    }

    // ============ A5-7: регион > 1000 символов -> дно работает (S6 без MaxSkip) ============
    // Module := ZeroOrMany(Number), короткий корректный префикс + >1000 символов хвостового мусора.
    // S6 не имеет MaxSkip (DefaultMaxSkip=1000) -> скан до EOF, хвост целиком покрыт абсорбером.
    [TestMethod]
    public void Test_S6_Region_LongerThanMaxSkip_FullyCovered()
    {
        var parser = new Parser(S6Terminals.Trivia());
        parser.Rules["Module"] = [new ZeroOrMany(S6Terminals.Number(), "Numbers")];
        parser.BuildTdoppRules();

        var input = "12 34 56 " + new string('#', 1500);
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end), $"expected Success, got {result.ResultKind}");
        Assert.AreEqual(input.Length, end, "S6 bottom must reach EOF");
        Assert.IsNull(parser.ErrorInfo);

        // абсорбер покрывает хвост длиннее DefaultMaxSkip=1000 - S6 истинное дно, не обрезано по MaxSkip.
        var absorber = FindAbsorbers(node).Single();
        Assert.AreEqual(input.Length, absorber.EndPos, "absorber must reach EOF");
        Assert.IsTrue(absorber.EndPos - absorber.StartPos > 1000, $"absorber must cover >1000 chars, got {absorber.EndPos - absorber.StartPos}");
    }

    // ============ A5-7: `}` внутри строки - задокументированное базовое поведение (R1/B3 уточнят) ============
    // `}` в стоп-наборе (Terminators кадра Body). В хвосте строка "abc}def" с `}` внутри.
    // Текущее поведение (скан не string-aware): есть абсорбер, оканчивающийся на `}` внутри строки
    // (скан остановился на `}`, а не прошёл строку целиком). R1/B3 (волны 3/5) сделают скан
    // string-aware -> тогда хвост покроется одним абсорбером до EOF (без остановки на `}` внутри строки).
    [TestMethod]
    public void Test_S6_ClosingBraceInsideString_DocumentedBaseline()
    {
        var parser = new Parser(S6Terminals.Trivia());
        parser.Rules["Item"] = [new Literal("x")];
        parser.Rules["Body"] = [new RecoveryRule(new OneOrMany(new Ref("Item"), "Items"), new RecoveryOptions { Terminators = [new Literal("}")] })];
        parser.Rules["Module"] = [new Seq([new Literal("{"), new Ref("Body")], "Module")];
        parser.BuildTdoppRules();

        var input = "{x \"abc}def\" GARBAGE";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end), $"expected Success, got {result.ResultKind}");
        Assert.AreEqual(input.Length, end, "bottom must reach EOF");

        // базовое поведение: скан не string-aware -> абсорбер оканчивается на `}` внутри строки.
        // (R1/B3 уточнят: после string-aware скана такого абсорбера не будет.)
        var bracePos = input.IndexOf('}');
        var absorbers = FindAbsorbers(node).ToList();
        Assert.IsTrue(
            absorbers.Any(a => a.EndPos == bracePos),
            $"current (non-string-aware) behavior: an absorber must end at the closing brace inside the string (pos {bracePos}); got [{string.Join(" ", absorbers.Select(a => a.StartPos + ".." + a.EndPos))}]");
    }

}

#endif
