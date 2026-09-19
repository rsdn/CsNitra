#if RECOVERY

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// 5a.2.4: S2-скан — прыжок к ближайшему multi-char Literal (IndexOf). Грамматика — как в TierBudgetTests:
// Module := '{' ZeroOrMany(Stmt) '}', Stmt := Ident ':' Expr ';', Expr — TDOPP. Якорь S2 — Ref("Stmt")
// (выведен из цикла Stmts). Мismatch внутри Expr (правый операнд '+' падает на мусор) → точка восстановления
// e на первом `#`; после мусора полный Stmt → Speculative на нём succeeds, T1 найден.
//
// Тест 1 (указанный вход): First(Stmt) = {Ident} (regex) — multi-char Literal в First-множестве якоря нет →
// предвычисленное множество пусто, прыжок неактивен: resync-позиция/результат те же для обоих входов.
// Тест 2 (девиация: Stmt := 'let' Ident ':' Expr ';' — multi-char ключевое слово в начале, First(Stmt) =
// {Literal("let")}): прыжок активен — скан прыгает от e прямо на вхождение `let`, S2ScanPositions < размера
// окна [e..t1]; БЕЗ прыжка тест падает (скан проходит по каждой позиции окна).
[TerminalMatcher]
public sealed partial class S2IndexOfTerminals
{
    [Regex(@"\d+")]
    public static partial Terminal Number();

    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

[TestClass]
public sealed class S2IndexOfTests
{
    // Грамматика TierBudgetTests; stmtWithKeyword — Stmt := 'let' Ident ':' Expr ';' (First(Stmt) =
    // {Literal("let")} — multi-char Literal, прыжок IndexOf активен).
    private static Parser NewParser(bool stmtWithKeyword)
    {
        var parser = new Parser(S2IndexOfTerminals.Trivia());
        parser.Rules["Expr"] = new Rule[]
        {
            S2IndexOfTerminals.Number(),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
            new Seq([new Ref("Expr"), new Literal("-"), new ReqRef("Expr", 100)], "Sub"),
            new Seq([new Ref("Expr"), new Literal("*"), new ReqRef("Expr", 200)], "Mul"),
            new Seq([new Ref("Expr"), new Literal("/"), new ReqRef("Expr", 200)], "Div"),
            new Seq([new Ref("Expr"), new Literal("=="), new ReqRef("Expr", 50)], "Eq"),
            new Seq([new Ref("Expr"), new Literal("!="), new ReqRef("Expr", 50)], "Neq"),
        };
        var stmt = stmtWithKeyword
            ? new Seq([new Literal("let"), S2IndexOfTerminals.Ident(), new Literal(":"), new Ref("Expr"), new Literal(";")], "Stmt")
            : new Seq([S2IndexOfTerminals.Ident(), new Literal(":"), new Ref("Expr"), new Literal(";")], "Stmt");
        parser.Rules["Stmt"] = [stmt];
        parser.Rules["Module"] = [new Seq([new Literal("{"), new ZeroOrMany(new Ref("Stmt"), "Stmts"), new Literal("}")], "Module")];
        parser.BuildTdoppRules();
        return parser;
    }

    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) {d.Message}"));

    private static (Parser Parser, string Diags) Parse(bool stmtWithKeyword, string input)
    {
        var parser = NewParser(stmtWithKeyword);
        parser.Parse(input, "Module", out _);
        return (parser, Describe(parser.RecoveryDiagnostics));
    }

    // Нормализованная диагностика: (Kind, длина, сообщение без абсолютных позиций). Skip-диагностики
    // ("skip to resync point", "skip to terminator", "bottom skip") начинаются в e (не сдвигается) и
    // заканчиваются в позиции после e (сдвинута на delta) — длина больше на delta, вычитаем.
    private static (RecoveryKind Kind, int Len, string Msg)[] Normalize(IReadOnlyList<RecoveryDiagnostic> diags, int shift)
        => diags
            .Select(d =>
            {
                var len = d.EndPos - d.StartPos;
                if (d.Kind == RecoveryKind.Skipped)
                    len -= shift;
                return (d.Kind, len, System.Text.RegularExpressions.Regex.Replace(d.Message, @"\d+", "N"));
            })
            .ToArray();

    // Указанный тест: полный Stmt после мусора — e = первый `#`, Speculative вызывается на `x` и succeeds,
    // T1 найден. (a) S2-скан срабатывает; (b) resync-позиция/T1-кандидат те же для обоих входов (IndexOf-
    // прыжок не меняет результат восстановления; First(Stmt) = {Ident} — прыжок неактивен, счётчики равны).
    [TestMethod]
    public void S2IndexOf_SpecifiedInput_SameResync()
    {
        const string noPadding = "{ a: 1+ ### x: 2 ; }";
        const string padded = "{ a: 1+ ###    x: 2 ; }"; // 3 пробела паддинга между ### и x (строго после e)
        const int delta = 3;

        var (p1, d1) = Parse(false, noPadding);
        var (p2, d2) = Parse(false, padded);

        Assert.IsTrue(p1.S2ScanPositions > 0,
            $"no-padding: S2ScanPositions={p1.S2ScanPositions} (S2 scan not triggered), diags: {d1}");
        Assert.IsTrue(p2.S2ScanPositions > 0,
            $"padded: S2ScanPositions={p2.S2ScanPositions} (S2 scan not triggered), diags: {d2}");

        // (b) тот же исход: одинаковая нормализованная диагностика...
        CollectionAssert.AreEqual(
            Normalize(p1.RecoveryDiagnostics, 0),
            Normalize(p2.RecoveryDiagnostics, delta),
            $"recovery outcome differs:\nno-padding: {d1}\npadded:     {d2}");

        // ...и resync-позиции (T1 "skip to resync point") сдвинуты ровно на delta.
        var resync1 = p1.RecoveryDiagnostics.Where(d => d.Message.StartsWith("skip to resync point")).Select(d => d.EndPos).ToArray();
        var resync2 = p2.RecoveryDiagnostics.Where(d => d.Message.StartsWith("skip to resync point")).Select(d => d.EndPos).ToArray();
        Assert.IsTrue(resync1.Length > 0, $"no S2 T1 resync diagnostics:\nno-padding: {d1}\npadded:     {d2}");
        CollectionAssert.AreEqual(resync1.Select(x => x + delta).ToArray(), resync2,
            $"resync positions not shifted by delta:\nno-padding: {d1}\npadded:     {d2}");
    }

    // Регрессионный тест прыжка: Stmt := 'let' Ident ':' Expr ';' (First(Stmt) = {Literal("let")} — multi-char
    // Literal). e = первый `#`, T1 на первом `let` после e. Прыжок IndexOf: скан проверяет e и позицию `let`
    // (S2ScanPositions = 2), пропуская позиции между. БЕЗ прыжка скан проходит по каждой позиции окна
    // [e..t1] (S2ScanPositions = t1 - e + 1) → ассерт падает. Паддинг строго после e (между ### и let):
    // результат тот же, resync-позиции сдвинуты на delta.
    [TestMethod]
    public void S2IndexOf_MultiCharKeyword_JumpSkipsPositions()
    {
        const string noPadding = "{ let a: 1+ ### let x: 2 ; }";
        const string padded = "{ let a: 1+ ###    let x: 2 ; }"; // 3 пробела паддинга между ### и let (строго после e)
        const int delta = 3;

        var (p1, d1) = Parse(true, noPadding);
        var (p2, d2) = Parse(true, padded);

        Assert.IsTrue(p1.S2ScanPositions > 0,
            $"no-padding: S2ScanPositions={p1.S2ScanPositions} (S2 scan not triggered), diags: {d1}");
        Assert.IsTrue(p2.S2ScanPositions > 0,
            $"padded: S2ScanPositions={p2.S2ScanPositions} (S2 scan not triggered), diags: {d2}");

        // (c) прыжок пропускает позиции: счётчик меньше размера окна [e..t1] (без прыжка счётчик == окну → падает).
        foreach (var (p, d, input) in new[] { (p1, d1, noPadding), (p2, d2, padded) })
        {
            var e = input.IndexOf('#');
            var t1 = input.IndexOf("let", e);
            var window = t1 - e + 1;
            Assert.IsTrue(p.S2ScanPositions < window,
                $"indexOf jump not effective: S2ScanPositions={p.S2ScanPositions}, window [e..t1] size={window} " +
                $"(e={e}, t1={t1}) — scan did not skip positions, diags: {d}");
        }

        // (b) тот же исход: одинаковая нормализованная диагностика...
        CollectionAssert.AreEqual(
            Normalize(p1.RecoveryDiagnostics, 0),
            Normalize(p2.RecoveryDiagnostics, delta),
            $"recovery outcome differs:\nno-padding: {d1}\npadded:     {d2}");

        // ...и resync-позиции (T1 "skip to resync point") сдвинуты ровно на delta.
        var resync1 = p1.RecoveryDiagnostics.Where(d => d.Message.StartsWith("skip to resync point")).Select(d => d.EndPos).ToArray();
        var resync2 = p2.RecoveryDiagnostics.Where(d => d.Message.StartsWith("skip to resync point")).Select(d => d.EndPos).ToArray();
        Assert.IsTrue(resync1.Length > 0, $"no S2 T1 resync diagnostics:\nno-padding: {d1}\npadded:     {d2}");
        CollectionAssert.AreEqual(resync1.Select(x => x + delta).ToArray(), resync2,
            $"resync positions not shifted by delta:\nno-padding: {d1}\npadded:     {d2}");
    }
}
#endif
