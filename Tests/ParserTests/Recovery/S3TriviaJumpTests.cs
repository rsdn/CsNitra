using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// 5a.2.3: S3-скан — счётчик позиций + trivia jump (решение (б): по всему trivia, вкл. комментарии).
// Грамматика — как в TierBudgetTests: Module := '{' ZeroOrMany(Stmt) '}', Stmt := Ident ':' Expr ';',
// Expr — TDOPP. Разница от TierBudgetTests: Trivia шире (\s* → \s + // и /* */), чтобы решение (б)
// было проверяемо: S3-скан пропускает trivia-бег (включая комментарии) одним Trivia.TryMatch, и
// скобки внутри /* ... */ больше не учитываются в curly/paren/bracket.
[TerminalMatcher]
public sealed partial class S3TriviaJumpTerminals
{
    [Regex(@"\d+")]
    public static partial Terminal Number();

    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"(//[^\n]*|/\*[^\*]*\*/|\s)*")]
    public static partial Terminal Trivia();
}

[TestClass]
public sealed class S3TriviaJumpTests
{
    private static Parser NewParser()
    {
        var parser = new Parser(S3TriviaJumpTerminals.Trivia());
        parser.Rules["Expr"] = new Rule[]
        {
            S3TriviaJumpTerminals.Number(),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
            new Seq([new Ref("Expr"), new Literal("-"), new ReqRef("Expr", 100)], "Sub"),
            new Seq([new Ref("Expr"), new Literal("*"), new ReqRef("Expr", 200)], "Mul"),
            new Seq([new Ref("Expr"), new Literal("/"), new ReqRef("Expr", 200)], "Div"),
            new Seq([new Ref("Expr"), new Literal("=="), new ReqRef("Expr", 50)], "Eq"),
            new Seq([new Ref("Expr"), new Literal("!="), new ReqRef("Expr", 50)], "Neq"),
        };
        parser.Rules["Stmt"] = [new Seq([S3TriviaJumpTerminals.Ident(), new Literal(":"), new Ref("Expr"), new Literal(";")], "Stmt")];
        parser.Rules["Module"] = [new Seq([new Literal("{"), new ZeroOrMany(new Ref("Stmt"), "Stmts"), new Literal("}")], "Module")];
        parser.BuildTdoppRules();
        return parser;
    }

    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) {d.Message}"));

    private static (Parser Parser, string Diags) Parse(string input)
    {
        var parser = NewParser();
        parser.Parse(input, "Module", out _);
        return (parser, Describe(parser.RecoveryDiagnostics));
    }

    // Nормализованная S3-диагностика: (Kind, сообщение без абсолютных позиций). Сравниваем только
    // S3 ("skip to terminator") — другие стратегии (S6-дно и т.п.) зависят от абсолютных позиций.
    private static string[] S3Skip(IReadOnlyList<RecoveryDiagnostic> diags)
        => diags
            .Where(d => d.Kind == RecoveryKind.Skipped && d.Message.StartsWith("skip to terminator"))
            .Select(d => System.Text.RegularExpressions.Regex.Replace(d.Message, @"\d+", "N"))
            .ToArray();

    // (a) S3-скан срабатывает (S3ScanPositions > 0); (b) S3-диагностика "skip to terminator" та же
    // для обоих входов (нормализованная); (c) trivia-бег (3 пробела) пропущен jump'ом: разница
    // счётчиков < 3.
    [TestMethod]
    public void S3TriviaJump_SameResync_FewerScannedPositions()
    {
        const string noPadding = "{ a: 1+ ### ; }";
        const string padded = "{ a: 1+ ###    ; }"; // 3 пробела МЕЖДУ ### и ; (строго после e)

        var (p1, d1) = Parse(noPadding);
        var (p2, d2) = Parse(padded);

        Assert.IsTrue(p1.S3ScanPositions > 0,
            $"no-padding: S3ScanPositions={p1.S3ScanPositions} (S3 scan not triggered), diags: {d1}");
        Assert.IsTrue(p2.S3ScanPositions > 0,
            $"padded: S3ScanPositions={p2.S3ScanPositions} (S3 scan not triggered), diags: {d2}");

        // (b) S3-диагностика "skip to terminator" та же (нормализованная).
        CollectionAssert.AreEqual(
            S3Skip(p1.RecoveryDiagnostics),
            S3Skip(p2.RecoveryDiagnostics),
            $"S3 diagnostics differ:\nno-padding: {d1}\npadded:     {d2}");

        // (c) trivia-бег пропущен: разница счётчиков < 3 (длина паддинга).
        var diff = p2.S3ScanPositions - p1.S3ScanPositions;
        Assert.IsTrue(diff < 3,
            $"trivia run not skipped: padded S3ScanPositions={p2.S3ScanPositions}, no-padding={p1.S3ScanPositions} (diff={diff}, expected < 3)\nno-padding: {d1}\npadded:     {d2}");
    }

    // Обязательный (решение (б)): S3 НЕ останавливается на закрывающей скобке внутри /* ... */ —
    // trivia-бег (весь комментарий) пропущен одним Trivia.TryMatch, скобка внутри комментария не
    // учитывается в curly. Resync-позиция совпадает с вариантом без комментария (сдвинута на длину
    // комментария). Без правки: S3ScanPositions==0 (нет jump'а) либо } внутри /* */ ложно учитывается
    // в curly и меняет resync-позицию.
    [TestMethod]
    public void S3TriviaJump_ClosingBraceInsideBlockComment_NotCounted()
    {
        const string noComment = "{ a: 1+ ### ; }";
        const string withComment = "{ a: 1+ ### /* } */ ; }";
        var delta = withComment.Length - noComment.Length; // длина вставленного " /* } */"

        var (p1, d1) = Parse(noComment);
        var (p2, d2) = Parse(withComment);

        Assert.IsTrue(p1.S3ScanPositions > 0,
            $"no-comment: S3ScanPositions={p1.S3ScanPositions} (S3 scan not triggered), diags: {d1}");
        Assert.IsTrue(p2.S3ScanPositions > 0,
            $"with-comment: S3ScanPositions={p2.S3ScanPositions} (S3 scan not triggered), diags: {d2}");

        // Resync-позиции (S3 "skip to terminator" EndPos) совпадают с сдвигом на delta: S3 не
        // остановился на } внутри комментария.
        var resync1 = p1.RecoveryDiagnostics
            .Where(d => d.Kind == RecoveryKind.Skipped && d.Message.StartsWith("skip to terminator"))
            .Select(d => d.EndPos)
            .ToArray();
        var resync2 = p2.RecoveryDiagnostics
            .Where(d => d.Kind == RecoveryKind.Skipped && d.Message.StartsWith("skip to terminator"))
            .Select(d => d.EndPos)
            .ToArray();
        Assert.IsTrue(resync1.Length > 0, $"no S3 resync diagnostics:\nno-comment: {d1}\nwith-comment: {d2}");
        Assert.IsTrue(resync1.Length == resync2.Length,
            $"S3 resync count differs:\nno-comment: {d1}\nwith-comment: {d2}");
        for (var i = 0; i < resync1.Length; i++)
            Assert.AreEqual(resync1[i] + delta, resync2[i],
                $"S3 resync position {i} not shifted by delta={delta} (stopped on a brace inside the block comment?):\nno-comment: {d1}\nwith-comment: {d2}");
    }
}
