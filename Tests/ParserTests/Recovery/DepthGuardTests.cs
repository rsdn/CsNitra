#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace MiniC;

// 7.1.1/R3: калибровка depth-guard.
// Эмпирика (docs/RecoveryImprovementProposal.md, §3.0a): грамматика `Expr := "(" Expr ")" | Digits`
// на ~1.5MB .NET 8 потоке переполняла стек реального вызова при 300 уровнях вложенности
// (295 OK, 300 — краш процесса), хотя per-char лимит (128 + 4*len) разрешал ~2900 кадров —
// бюджет стека потока это ФИКСИРОВАННОЕ число кадров (~575-615), не функция длины входа.
// Фикс: абсолютный потолок RecoveryProfile.MaxParseDepthCap (400, ~30% запаса под порогом
// переполнения) + профилактическая проверка остатка стека (RuntimeHelpers.
// EnsureSufficientExecutionStack, паттерн Roslyn StackGuard) в BeginParseFrame.
// Инвариант guard'а: BeginParseFrame возвращает true БЕЗ инкремента (старый increment-then-check
// протекал +1 за каждый отклонённый кадр — ParseAlternative возвращает Failure до finally),
// поэтому MaxParseDepthReached останавливается ровно на потолке.
[TestClass]
public sealed class DepthGuardTests
{
    private static Parser MakeParser()
    {
        var parser = new Parser(Terminals.Trivia());
        parser.Rules["Expr"] =
        [
            new Seq([new Literal("("), new Ref("Expr"), new Literal(")")], "Parens"),
            Terminals.Number(),
        ];
        parser.BuildTdoppRules();
        return parser;
    }

    private static string Nested(int levels) => new string('(', levels) + "1" + new string(')', levels);

    // ============ R3: вход из эмпирики (350 уровней) не роняет host — guard срабатывает первым ============
    // До 7.1.1 этот вход крашил процесс (StackOverflowException во время recovery re-parse).
    // После: depth-счётчик останавливается ровно на потолке (guard отклоняет следующий кадр),
    // Parse возвращает recovered-результат (диагностика присутствует) вместо чистого успеха.
    [TestMethod]
    public void Test_DeepNesting_GuardFires_NoCrash()
    {
        var parser = MakeParser();
        var input = Nested(350);
        var result = parser.Parse(input, "Expr", out _);

        Assert.AreEqual(RecoveryProfile.MaxParseDepthCap, parser.MaxParseDepthReached,
            "depth-счётчик должен остановиться ровно на откалиброванном потолке (guard отклоняет следующий кадр)");
        // 7.1.2/R3: guard-fired result = A5-4 bottom contract — Success<T> c деревом, покрывающим весь
        // ввод (recovery-to-EOF/S6), а не bare Failure; ErrorInfo = null (recovered).
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length
            && node.StartPos == 0 && node.EndPos == input.Length,
            $"guard сработал → дно-контракт A5-4: Success@EOF с деревом на весь ввод (не bare Failure), got {result.ResultKind}@{result.NewPos}");
        Assert.IsNull(parser.ErrorInfo, "recovered (дно-контракт) → ErrorInfo = null");
        // 7.1.2/R3: ровно одна диагностика InsufficientStack в публичном списке (на точке срабатывания guard'а).
        var insufficient = parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.InsufficientStack).ToList();
        Assert.AreEqual(1, insufficient.Count,
            $"guard сработал → ровно одна диагностика InsufficientStack (RecoveryDiagnostics={parser.RecoveryDiagnostics.Count})");
    }

    // ============ Контроль: легитимный parse под потолком проходит чисто (guard и recovery не мешают) ============
    // 50 уровней = 102 кадра (2 кадра на уровень + 2) — далеко под потолком 400.
    [TestMethod]
    public void Test_ShallowNesting_CleanParse()
    {
        var parser = MakeParser();
        var input = Nested(50);
        var result = parser.Parse(input, "Expr", out _);

        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == input.Length,
            $"Expected Success@EOF, got {result.ResultKind}@{result.NewPos}");
        Assert.AreEqual(0, parser.RecoveryDiagnostics.Count);
        // 7.1.2/R3: guard не сработал (глубина под потолком) → диагностика InsufficientStack отсутствует.
        Assert.IsFalse(parser.RecoveryDiagnostics.Any(d => d.Kind == RecoveryKind.InsufficientStack),
            "guard не сработал (под потолком) → диагностика InsufficientStack отсутствует");
        Assert.IsNull(parser.ErrorInfo);
        Assert.AreEqual(102, parser.MaxParseDepthReached, "2 кадра на уровень вложенности + 2");
    }
}
