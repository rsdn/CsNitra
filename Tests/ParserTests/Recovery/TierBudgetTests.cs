#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TerminalMatcher]
public sealed partial class TierBudgetTerminals
{
    [Regex(@"\d+")]
    public static partial Terminal Number();

    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

// A2: бюджеты по тирам (не по кандидатам). Регресс «мусор с identifier-токенами»: S1 генерирует
// МНОГО кандидатов вставки (большой FollowSet верхнего правила Expr), но S3 (panic, ранг 3) всё
// равно пробуются — под-бюджет S1 не глушит S3. В старой модели (пер-позиционный бюджет кандидатов
// = 3) S3 голодал: S0 + 2×S1 выедали бюджет раньше, чем цикл дошёл бы до S3 (ранг 3).
[TestClass]
public sealed class TierBudgetTests
{
    // Module := '{' ZeroOrMany(Stmt) '}' ; Stmt := Ident ':' Expr ';' ; Expr — TDOPP с шестью
    // операторами, поэтому FollowSet(Expr) велик ({;,+,-,*,/,==,!=}) и S1 порождает много кандидатов
    // вставки (ранг 1). Блок '{'…'}' нужен, чтобы у S2 (resync) НЕ было якоря после мусора (First(Stmt)
    // = {Ident} не совпадает с мусором/';'), а у S3 (panic) был терминатор ';' — так S3 становится
    // единственным реальным ремонтом.
    private static Parser NewParser()
    {
        var parser = new Parser(TierBudgetTerminals.Trivia());
        parser.Rules["Expr"] = new Rule[]
        {
            TierBudgetTerminals.Number(),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
            new Seq([new Ref("Expr"), new Literal("-"), new ReqRef("Expr", 100)], "Sub"),
            new Seq([new Ref("Expr"), new Literal("*"), new ReqRef("Expr", 200)], "Mul"),
            new Seq([new Ref("Expr"), new Literal("/"), new ReqRef("Expr", 200)], "Div"),
            new Seq([new Ref("Expr"), new Literal("=="), new ReqRef("Expr", 50)], "Eq"),
            new Seq([new Ref("Expr"), new Literal("!="), new ReqRef("Expr", 50)], "Neq"),
        };
        parser.Rules["Stmt"] = [new Seq([TierBudgetTerminals.Ident(), new Literal(":"), new Ref("Expr"), new Literal(";")], "Stmt")];
        parser.Rules["Module"] = [new Seq([new Literal("{"), new ZeroOrMany(new Ref("Stmt"), "Stmts"), new Literal("}")], "Module")];
        parser.BuildTdoppRules();
        return parser;
    }

    // Mismatch ВНУТРИ Expr (правый операнд '+' не совпал с мусором) → верхнее правило Expr с большим
    // FollowSet → S1 генерирует много кандидатов. S3 (panic) — единственный реальный ремонт: он
    // прокидывает скан до терминатора ';' и даёт прогресс. С под-бюджетами по тирам S3 достижим,
    // хотя S1 (много вставок) выедает СВОЙ под-бюджет, а не общий.
    [TestMethod]
    public void Test_S1ManyCandidates_S3StillTried_NotStarved()
    {
        var parser = NewParser();
        // Старый пер-позиционный бюджет НЕ ставится: по тирам S3 достижим по умолчанию.
        var input = "{ a: 1+ ### ; }";
        var result = parser.Parse(input, "Module", out _);

        // S3 (ранг 3, panic) — диагностика "skip to terminator": S3 принят (не заглушен под-бюджетом S1).
        var s3 = parser.RecoveryDiagnostics
            .Where(d => d.Kind == RecoveryKind.Skipped && d.Message.Contains("skip to terminator"))
            .ToList();
        Assert.IsTrue(s3.Count >= 1,
            $"Expected >= 1 S3 'skip to terminator' diagnostic (S3 not starved by the S1 sub-budget), got: " +
            $"{Describe(parser.RecoveryDiagnostics)} result={result.ResultKind}@{result.NewPos}/{result.MaxFailPos} " +
            $"ErrorInfo={parser.ErrorInfo?.Pos} passes={parser.RecoveryPasses} gen={parser.EngineGenerateCalls}");
    }

    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) {d.Message}"));
}
