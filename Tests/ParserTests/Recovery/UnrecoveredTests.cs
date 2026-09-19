#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
namespace Recovery;

[TerminalMatcher]
public sealed partial class UnrecoveredTerminals
{
    [Regex(@"\d+")]
    public static partial Terminal Number();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

// 1.3.4: S6 (ранг 6) — дно, а не восстановление. Когда S6 — fallback (принятый кандидат), выдаётся
// RecoveryDiagnostic(Kind.Unrecovered, E, …) в точке восстановления E. Gate — candidate.Rank == 6
// (НЕ бюджетная эвристика attempts.Count > MaxRecoveryAttemptsPerPosition): цикл пробует кандидатов
// по порядку и выходит при первом дающем прогресс, поэтому если принят S6 — ни один ремонтный
// (S0–S5) прогресса не дал.
[TestClass]
public sealed class UnrecoveredTests
{
    // ============ 1. S6 — fallback: хвостовой мусор после полной структуры (snapshot == null) ============
    // Module := Expr Expr, Expr := Number. Вход "12 34 ###": Module завершается после двух Number,
    // хвост "###" не совпадает ни с одним ожидаемым терминалом. Разбор — Success ниже EOF БЕЗ
    // mismatch (snapshot == null) → ремонтные кандидаты S1–S5 не генерируются, S6 — ЕДИНСТВЕННЫЙ
    // кандидат, а значит и fallback, независимо от бюджета (попытки на точке: S0 + S6 = 2 ≤ дефолт 3).
    [TestMethod]
    public void Test_S6_Fallback_EmitsUnrecovered()
    {
        var parser = new Parser(UnrecoveredTerminals.Trivia());
        parser.Rules["Expr"] = [UnrecoveredTerminals.Number()];
        parser.Rules["Module"] = [new Seq([new Ref("Expr"), new Ref("Expr")], "Module")];
        parser.BuildTdoppRules();

        var input = "12 34 ###";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out _, out var end), $"expected Success, got {result.ResultKind}");
        Assert.AreEqual(input.Length, end, "S6 bottom must reach EOF");
        Assert.IsNull(parser.ErrorInfo);

        var unrecovered = parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Unrecovered).ToList();
        Assert.IsTrue(unrecovered.Count >= 1,
            $"expected >= 1 Unrecovered diagnostic (S6 is the fallback), got: {Describe(parser.RecoveryDiagnostics)}");
    }

    // ============ 1b. Позиция Unrecovered = точка восстановления E (начало хвостового мусора) ============

    [TestMethod]
    public void Test_S6_Fallback_UnrecoveredAtRecoveryPoint()
    {
        var parser = new Parser(UnrecoveredTerminals.Trivia());
        parser.Rules["Expr"] = [UnrecoveredTerminals.Number()];
        parser.Rules["Module"] = [new Seq([new Ref("Expr"), new Ref("Expr")], "Module")];
        parser.BuildTdoppRules();

        var input = "12 34 ###";
        parser.Parse(input, "Module", out _);

        var unrecovered = parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Unrecovered).ToList();
        Assert.IsTrue(unrecovered.Count >= 1, $"expected >= 1 Unrecovered, got: {Describe(parser.RecoveryDiagnostics)}");
        // E — граница валидного префикса ("12 34") и мусора ("###"): мусор начинается в input.IndexOf("###").
        var e = input.IndexOf("###");
        Assert.IsTrue(unrecovered.Any(d => d.StartPos == e),
            $"expected Unrecovered at recovery point E={e}, got: {Describe(parser.RecoveryDiagnostics)}");
    }

    // ============ 1c. Независимость от бюджета: Unrecovered при дефолтном (3) и большом бюджете ============

    [TestMethod]
    public void Test_S6_Fallback_UnrecoveredIndependentOfBudget()
    {
        foreach (var budget in new[] { 3, 16, 1000 })
        {
            var parser = new Parser(UnrecoveredTerminals.Trivia()) { MaxRecoveryAttemptsPerPosition = budget };
            parser.Rules["Expr"] = [UnrecoveredTerminals.Number()];
            parser.Rules["Module"] = [new Seq([new Ref("Expr"), new Ref("Expr")], "Module")];
            parser.BuildTdoppRules();

            var input = "12 34 ###";
            var result = parser.Parse(input, "Module", out _);

            Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == input.Length,
                $"budget={budget}: expected Success@EOF, got {result.ResultKind}");
            var unrecovered = parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Unrecovered).ToList();
            Assert.IsTrue(unrecovered.Count >= 1,
                $"budget={budget}: expected >= 1 Unrecovered (S6 is the fallback regardless of budget), got: {Describe(parser.RecoveryDiagnostics)}");
        }
    }

    // ============ 2. Ремонтный кандидат (S1) принят: Unrecovered НЕ выдаётся ============
    // Module := a b c, вход "a c" (пропущен "b"). Mismatch в точке "b" → S1 (ранг 1) вставляет "b"
    // → Success@EOF. Принят ремонтный кандидат S1 (не S6) → Unrecovered НЕ выдаётся.
    [TestMethod]
    public void Test_RepairCandidate_Accepted_NoUnrecovered()
    {
        var parser = new Parser(UnrecoveredTerminals.Trivia());
        parser.Rules["Module"] = [new Seq([new Literal("a"), new Literal("b"), new Literal("c")], "Module")];
        parser.BuildTdoppRules();

        var input = "a c";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out _, out var end), $"expected Success, got {result.ResultKind}");
        Assert.AreEqual(input.Length, end, "repair must reach EOF");
        Assert.IsNull(parser.ErrorInfo);

        // Recovery действительно сработала (принят ремонтный кандидат с диагностикой),
        // а не чистый успех — иначе тест тривиален.
        var repair = parser.RecoveryDiagnostics.Where(d => d.Kind is RecoveryKind.Inserted or RecoveryKind.Skipped).ToList();
        Assert.IsTrue(repair.Count >= 1, $"expected a repair (Inserted/Skipped) diagnostic, got: {Describe(parser.RecoveryDiagnostics)}");

        var unrecovered = parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Unrecovered).ToList();
        Assert.AreEqual(0, unrecovered.Count,
            $"a repair candidate (S1) was accepted, so no Unrecovered diagnostic is expected, got: {Describe(parser.RecoveryDiagnostics)}");
    }

    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) rule={d.RuleName ?? "-"} {d.Message}"));
}

#endif
