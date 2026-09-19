#if RECOVERY

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// 5a.2.2: S2-скан — счётчик позиций + trivia jump. Грамматика — как в TierBudgetTests:
// Module := '{' ZeroOrMany(Stmt) '}', Stmt := Ident ':' Expr ';', Expr — TDOPP. Якорь S2 —
// Ref("Stmt") (выведен из цикла Stmts), First(Stmt) = {Ident}. Мismatch внутри Expr (правый
// операнд '+' падает на мусор) → точка восстановления e на первом `#` (trivia перед мусором
// уже пропущен парсером); S2-скан идёт от e до maxS, FirstMatchesAt срабатывает на `b`
// (Speculative("Stmt") не удаётся — Stmt требует ':'), скан доходит до maxS без T1. Реальный
// resync даёт S3 (терминаторы): цепочка skips b (Ident) → ; → } → EOF + S6-дно.
[TerminalMatcher]
public sealed partial class S2TriviaJumpTerminals
{
    [Regex(@"\d+")]
    public static partial Terminal Number();

    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

[TestClass]
public sealed class S2TriviaJumpTests
{
    private static Parser NewParser()
    {
        var parser = new Parser(S2TriviaJumpTerminals.Trivia());
        parser.Rules["Expr"] = new Rule[]
        {
            S2TriviaJumpTerminals.Number(),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
            new Seq([new Ref("Expr"), new Literal("-"), new ReqRef("Expr", 100)], "Sub"),
            new Seq([new Ref("Expr"), new Literal("*"), new ReqRef("Expr", 200)], "Mul"),
            new Seq([new Ref("Expr"), new Literal("/"), new ReqRef("Expr", 200)], "Div"),
            new Seq([new Ref("Expr"), new Literal("=="), new ReqRef("Expr", 50)], "Eq"),
            new Seq([new Ref("Expr"), new Literal("!="), new ReqRef("Expr", 50)], "Neq"),
        };
        parser.Rules["Stmt"] = [new Seq([S2TriviaJumpTerminals.Ident(), new Literal(":"), new Ref("Expr"), new Literal(";")], "Stmt")];
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

    // Нормализованная диагностика: (Kind, длина, сообщение без абсолютных позиций). S6 ("bottom skip")
    // поглощает весь вход, поэтому его длина больше на delta (величину паддинга) — вычитаем.
    private static (RecoveryKind Kind, int Len, string Msg)[] Normalize(IReadOnlyList<RecoveryDiagnostic> diags, int shift)
        => diags
            .Select(d =>
            {
                var len = d.EndPos - d.StartPos;
                if (d.Message.StartsWith("bottom skip"))
                    len -= shift;
                return (d.Kind, len, System.Text.RegularExpressions.Regex.Replace(d.Message, @"\d+", "N"));
            })
            .ToArray();

    // (a) S2-скан срабатывает (доходит до Speculative на `b`); (b) trivia-паддинг НЕ меняет
    // результат восстановления (структура диагностики та же, resync-позиции сдвинуты на величину
    // паддинга); (c) trivia-бег пропущен jump'ом: разница счётчиков < длины паддинга.
    [TestMethod]
    public void S2TriviaJump_SameResync_FewerScannedPositions()
    {
        const string noPadding = "{ a: 1+ ### b ; }";
        const string padded = "{ a: 1+   ### b ; }";
        const int delta = 2; // 3 пробела вместо 1

        var (p1, d1) = Parse(noPadding);
        var (p2, d2) = Parse(padded);

        Assert.IsTrue(p1.S2ScanPositions > 0,
            $"no-padding: S2ScanPositions={p1.S2ScanPositions} (S2 scan not triggered), diags: {d1}");
        Assert.IsTrue(p2.S2ScanPositions > 0,
            $"padded: S2ScanPositions={p2.S2ScanPositions} (S2 scan not triggered), diags: {d2}");

        // (b) тот же исход: одинаковая нормализованная диагностика...
        CollectionAssert.AreEqual(
            Normalize(p1.RecoveryDiagnostics, 0),
            Normalize(p2.RecoveryDiagnostics, delta),
            $"recovery outcome differs:\nno-padding: {d1}\npadded:     {d2}");

        // ...и resync-позиции (S3 "skip to terminator") сдвинуты ровно на delta.
        var resync1 = p1.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Skipped && d.Message.StartsWith("skip to terminator")).Select(d => d.EndPos).ToArray();
        var resync2 = p2.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Skipped && d.Message.StartsWith("skip to terminator")).Select(d => d.EndPos).ToArray();
        Assert.IsTrue(resync1.Length > 0, $"no S3 resync diagnostics:\nno-padding: {d1}\npadded:     {d2}");
        CollectionAssert.AreEqual(resync1.Select(x => x + delta).ToArray(), resync2,
            $"resync positions not shifted by delta:\nno-padding: {d1}\npadded:     {d2}");

        // (c) trivia-бег пропущен: разница счётчиков < 3 (длина паддинга).
        var diff = p2.S2ScanPositions - p1.S2ScanPositions;
        Assert.IsTrue(diff < 3,
            $"trivia run not skipped: padded S2ScanPositions={p2.S2ScanPositions}, no-padding={p1.S2ScanPositions} (diff={diff}, expected < 3)\nno-padding: {d1}\npadded:     {d2}");
    }

    // Паддинг ВНУТРИ S2-скан-окна [e..maxS] (между мусором и `b` — после точки восстановления e):
    // без trivia jump скан проходит по каждому пробелу паддинга (diff == величина паддинга, тест
    // падает), с jump'ом бег пропущен одним Trivia.TryMatch (diff < 3). Это регрессионный тест jump'а.
    [TestMethod]
    public void S2TriviaJump_PaddingInsideScanWindow_IsSkipped()
    {
        const string noPadding = "{ a: 1+ ### b ; }";
        const string padded = "{ a: 1+ ###    b ; }"; // 4 пробела (величина паддинга 3) между ### и b
        const int delta = 3;

        var (p1, d1) = Parse(noPadding);
        var (p2, d2) = Parse(padded);

        Assert.IsTrue(p1.S2ScanPositions > 0,
            $"no-padding: S2ScanPositions={p1.S2ScanPositions} (S2 scan not triggered), diags: {d1}");
        Assert.IsTrue(p2.S2ScanPositions > 0,
            $"padded: S2ScanPositions={p2.S2ScanPositions} (S2 scan not triggered), diags: {d2}");

        // Тот же исход: одинаковая последовательность терминаторов S3, resync-позиции сдвинуты на delta.
        var term1 = p1.RecoveryDiagnostics.Where(d => d.Message.StartsWith("skip to terminator")).Select(d => d.Message).ToArray();
        var term2 = p2.RecoveryDiagnostics.Where(d => d.Message.StartsWith("skip to terminator")).Select(d => d.Message).ToArray();
        CollectionAssert.AreEqual(term1, term2, $"terminator sequence differs:\nno-padding: {d1}\npadded:     {d2}");
        var resync1 = p1.RecoveryDiagnostics.Where(d => d.Message.StartsWith("skip to terminator")).Select(d => d.EndPos).ToArray();
        var resync2 = p2.RecoveryDiagnostics.Where(d => d.Message.StartsWith("skip to terminator")).Select(d => d.EndPos).ToArray();
        CollectionAssert.AreEqual(resync1.Select(x => x + delta).ToArray(), resync2,
            $"resync positions not shifted by delta:\nno-padding: {d1}\npadded:     {d2}");

        // Trivia-бег пропущен: разница счётчиков < 3. БЕЗ jump'а diff == 3 (тест падает).
        var diff = p2.S2ScanPositions - p1.S2ScanPositions;
        Assert.IsTrue(diff < 3,
            $"trivia run not skipped: padded S2ScanPositions={p2.S2ScanPositions}, no-padding={p1.S2ScanPositions} (diff={diff}, expected < 3)\nno-padding: {d1}\npadded:     {d2}");
    }
}
#endif
