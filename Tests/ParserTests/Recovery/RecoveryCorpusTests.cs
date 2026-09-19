#nullable enable

using System.Diagnostics;
using System.Text;
using ExtensibleParser;

#if RECOVERY
namespace Recovery;

[TerminalMatcher]
public sealed partial class CorpusTerminals
{
    [Regex(@"\d+")]
    public static partial Terminal Number();

    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();

    [Regex(@"[^\w\s]+")]
    public static partial RecoveryTerminal ErrorOperator();

    public static Terminal ErrorEmpty() => _error;

    private static Terminal _error = new EmptyTerminal("Error");
}

// D1-корпус (волна 0, база D1 + D2-ядро): 7 детерминированных сценариев + отчёт счётчиков.
// Каждый сценарий: генератор входа + грамматика + замер (RecoveryPasses, EngineGenerateCalls,
// Memo.Count, Success@EOF, min wall-time из K прогонов). Тесты stateless/thread-safe: каждый
// строит свой Parser. Wall-time — только в отчёте (Trace), никогда в ассертах (флакал под
// параллельной нагрузкой, см. RecoveryPerfTests). Ассерты — только на детерминированных величинах.
[TestClass]
public sealed class RecoveryCorpusTests
{
    // ============ Грамматики ============

    // МиниC (по образцу RecoveryPerfTests): для D1.1/D1.2/D1.6.
    private static Parser NewMiniCParser()
    {
        var parser = new Parser(CorpusTerminals.Trivia());
        var closingBrace = new OftenMissed(new Literal("}"));
        var semicolon = new OftenMissed(new Literal(";"));

        parser.Rules["Expr"] = new Rule[]
        {
            CorpusTerminals.Number(),
            CorpusTerminals.Ident(),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
            new Seq([new Ref("Expr"), new Literal("="), new ReqRef("Expr", 10, Right: true)], "Assignment"),

            // Recovery rules:
            new Seq([new Ref("Expr"), CorpusTerminals.ErrorOperator(), new ReqRef("Expr", 200)], "RecoveryOperator"),
            new Seq([new Ref("Expr"), CorpusTerminals.ErrorEmpty(), new ReqRef("Expr", 200)], "RecoveryEmptyOperator"),
            CorpusTerminals.ErrorEmpty(),
        };

        parser.Rules["Statement"] = new Rule[]
        {
            new Seq([new Literal("int"), CorpusTerminals.Ident(), semicolon], "VarDecl"),
            new Seq([new Ref("Expr"), semicolon], "ExprStmt"),
        };

        parser.Rules["Block"] = new Rule[]
        {
            new Seq([new Literal("{"), new ZeroOrMany(new Seq([new NotPredicate(new Ref("Function")), new Ref("Statement")], "BlockStatement")), closingBrace], "MultiBlock"),
            new Ref("Statement", "SimplBlock"),
        };

        parser.Rules["Function"] = new Rule[]
        {
            new Seq([new Literal("int"), CorpusTerminals.Ident(), new Literal("("), new Literal(")"), new Ref("Block")], "FunctionDecl"),
        };

        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Function"), "ModuleFunctions")];

        parser.BuildTdoppRules();
        return parser;
    }

    // Вложенные циклы Module(ZeroOrMany)→Block(OneOrMany)→Statement. Тело внутреннего цикла —
    // Ref(Statement), Kind Seq == имя Ref-тела, чтобы сработал loop-level абсорбер 3.0c при topIdx==0.
    // Без закрывающего `}`: иначе mismatch на `}` перезаписывает снимок кадром Block (не Statement).
    // Для D1.3 (ошибка в начале итерации).
    private static Parser NewNestedLoopParser()
    {
        var parser = new Parser(CorpusTerminals.Trivia());
        parser.MaxRecoveryAttemptsPerPosition = 16;
        parser.Rules["Statement"] = [new Seq([new Literal("int"), CorpusTerminals.Ident(), new Literal(";")], "Statement")];
        parser.Rules["Block"] = [new OneOrMany(new Ref("Statement"), "Statements")];
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Block"), "Blocks")];
        parser.BuildTdoppRules();
        return parser;
    }

    // Regex-терминалы (Ident — DFA), терминатор — regex → S3 сканирует DFA. Для D1.4.
    private static Parser NewRegexParser()
    {
        var parser = new Parser(CorpusTerminals.Trivia());
        // Глубокий сценарий: S2/S3 — попытка 3+, дефолтный бюджет 3 их глушит (см. Parser.Recovery.cs:39).
        parser.MaxRecoveryAttemptsPerPosition = 16;
        parser.Rules["Record"] = [new Seq([CorpusTerminals.Ident(), new Literal(";")], "Record")];
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Record"), "Records")];
        parser.BuildTdoppRules();
        return parser;
    }

    // Longest-match: две альтернативы с пересекающимся префиксом (Seq(a,b) длиннее Seq(a)).
    // Движок выбирает длиннейший; равной длины нет → ошибки амбивалентности нет. Для D1.5.
    private static Parser NewLongestMatchParser()
    {
        var parser = new Parser(CorpusTerminals.Trivia());
        parser.MaxRecoveryAttemptsPerPosition = 16;
        parser.Rules["Item"] = new Rule[]
        {
            new Seq([new Literal("a"), new Literal("b")], "Long"),
            new Seq([new Literal("a")], "Short"),
        };
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Item"), "Items")];
        parser.BuildTdoppRules();
        return parser;
    }

    // Двойное повреждение: мусор + битая следующая конструкция (Construct = int Ident ;).
    // Приёмочный тест волны 5 (A5-1); в волне 0 — baseline. Для D1.7.
    private static Parser NewDoubleDamageParser()
    {
        var parser = new Parser(CorpusTerminals.Trivia());
        // Двойное повреждение: 2+ точки recovery, S2/S3 — попытка 3+ → явный бюджет.
        parser.MaxRecoveryAttemptsPerPosition = 16;
        var semicolon = new OftenMissed(new Literal(";"));
        parser.Rules["Construct"] = [new Seq([new Literal("int"), CorpusTerminals.Ident(), semicolon], "Construct")];
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Construct"), "Constructs")];
        parser.BuildTdoppRules();
        return parser;
    }

    // ============ Генераторы входов (детерминированные) ============

    // D1.1: N функций, в каждой пропущена ; между int a и int b (N мелких ошибок).
    private static string GenManyErrors(int n)
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= n; i++)
            sb.Append($"int f{i}() {{ int a{i} int b{i}; }}");
        return sb.ToString();
    }

    // D1.2: correctCount корректных функций + ровно одна битая (пропущена ;) в самом конце.
    private static string GenOneErrorAtEnd(int correctCount)
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= correctCount; i++)
            sb.Append($"int f{i}() {{ int a{i}; int b{i}; }}");
        sb.Append($"int f{correctCount + 1}() {{ int a{correctCount + 1} int b{correctCount + 1}; }}");
        return sb.ToString();
    }

    // D1.3: мусор $ в начале итерации внутреннего цикла (Statement внутри Block внутри Module).
    private static string GenNestedLoopError() => "int a; $ int b;";

    // D1.4: мусор @ в середине (regex-терминалы).
    private static string GenRegexError() => "a ; b @ c ; d ;";

    // D1.5: мусор $ в середине (longest-match).
    private static string GenLongestMatchError() => "a b $ a b";

    // D1.6: «грязный» файл — пропущена ; в каждой every-й функции (ошибки равномерно).
    private static string GenDirtyFile(int total, int every)
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= total; i++)
            sb.Append(i % every == 0
                ? $"int f{i}() {{ int a{i} int b{i}; }}"
                : $"int f{i}() {{ int a{i}; int b{i}; }}");
        return sb.ToString();
    }

    // D1.7: двойное повреждение — прогон мусора @@@, затем битая конструкция (int b $ c;).
    private static string GenDoubleDamage() => "int a; @@@ int b $ c;";

    // ============ Замер (D2-ядро) ============

    private sealed record ScenarioResult(string Name, bool SuccessAtEof, int Passes, int GenCalls, int MemoCount, double MinTimeMs, int ResyncPos);

    // Минимум wall-time из K прогонов (снижает шум); счётчики берутся из последнего прогона (детерминированы).
    // ResyncPos — точка, куда recovery пришёл после первого пропуска (первый Skipped-диагностик, EndPos);
    // для D1.7 это старт битой конструкции (метрика волны 5), -1 если пропуска не было.
    private static ScenarioResult Measure(string name, Func<Parser> newParser, string input, int runs = 5)
    {
        var minTime = double.MaxValue;
        var passes = 0;
        var memoCount = 0;
        var successAtEof = false;
        var genCalls = 0;
        var resyncPos = -1;

        for (var r = 0; r < runs; r++)
        {
            var parser = newParser();
            var sw = Stopwatch.StartNew();
            var result = parser.Parse(input, "Module", out _);
            sw.Stop();
            minTime = Math.Min(minTime, sw.Elapsed.TotalMilliseconds);
            passes = parser.RecoveryPasses;
            memoCount = parser.Memo.Count;
            genCalls = parser.EngineGenerateCalls;
            successAtEof = result.TryGetSuccess(out _, out var end) && end == input.Length;
            resyncPos = parser.RecoveryDiagnostics
                .FirstOrDefault(d => d.Kind == ExtensibleParser.Recovery.RecoveryKind.Skipped)?.EndPos ?? -1;
        }

        return new ScenarioResult(name, successAtEof, passes, genCalls, memoCount, minTime, resyncPos);
    }

    private static string ReportLine(ScenarioResult m) =>
        $"{m.Name,-8} success@EOF={m.SuccessAtEof,-5} passes={m.Passes,-5} gen={m.GenCalls,-5} memo={m.MemoCount,-6} time={m.MinTimeMs,9:F3}ms resync={m.ResyncPos}";

    // ============ Сценарии D1_1 .. D1_7 ============

    [TestMethod]
    public void Test_D1_1_ManySmallErrors()
    {
        const int n = 120;
        var m = Measure("D1.1", NewMiniCParser, GenManyErrors(n));
        Trace.WriteLine(ReportLine(m));

        // D1.1 — приёмка 1.3.2: 120 мелких ошибок → Success@EOF. MaxRecoveryIterations=1000 >= 120,
        // S6 (ранг 6) исключён из бюджета на точку (гарантированное дно) → каждый проход чинит >=1 ошибку
        // и итерации доходят до EOF.
        Assert.IsTrue(m.SuccessAtEof, $"D1.1 not recovered to EOF\n{ReportLine(m)}");
    }

    [TestMethod]
    public void Test_D1_2_OneErrorAtEnd()
    {
        const int correct = 500;
        var m = Measure("D1.2", NewMiniCParser, GenOneErrorAtEnd(correct));
        Trace.WriteLine(ReportLine(m));

        // B1 (точная hygiene): большой корректный префикс мемоизируется, одна ошибка в конце → Success@EOF.
        Assert.IsTrue(m.SuccessAtEof, $"D1.2 not recovered to EOF\n{ReportLine(m)}");
        Assert.IsTrue(m.Passes <= 2, $"D1.2 passes={m.Passes} > 2 (одна ошибка)\n{ReportLine(m)}");
    }

    [TestMethod]
    public void Test_D1_3_NestedLoopError()
    {
        var m = Measure("D1.3", NewNestedLoopParser, GenNestedLoopError());
        Trace.WriteLine(ReportLine(m));

        // Ошибка в начале итерации вложенного цикла → loop-level абсорбер (3.0c) → Success@EOF.
        Assert.IsTrue(m.SuccessAtEof, $"D1.3 not recovered to EOF\n{ReportLine(m)}");
    }

    [TestMethod]
    public void Test_D1_4_RegexTerminals()
    {
        var m = Measure("D1.4", NewRegexParser, GenRegexError());
        Trace.WriteLine(ReportLine(m));

        // Regex-терминалы (Ident — DFA): терминатор/follow — regex, сканы S2/S3 идут через DFA,
        // follow-набор неточный → всё равно Success@EOF.
        Assert.IsTrue(m.SuccessAtEof, $"D1.4 not recovered to EOF\n{ReportLine(m)}");
    }

    [TestMethod]
    public void Test_D1_5_AmbiguousLongestMatch()
    {
        var m = Measure("D1.5", NewLongestMatchParser, GenLongestMatchError());
        Trace.WriteLine(ReportLine(m));

        // Longest-match: движок выбирает длиннейшую альтернативу, без ошибки равной длины → Success@EOF.
        Assert.IsTrue(m.SuccessAtEof, $"D1.5 not recovered to EOF\n{ReportLine(m)}");
    }

    [TestMethod]
    public void Test_D1_6_DirtyFile()
    {
        const int total = 50;
        const int every = 5;
        var errorCount = total / every;
        var m = Measure("D1.6", NewMiniCParser, GenDirtyFile(total, every));
        Trace.WriteLine(ReportLine(m));

        // B5 (кластеризация): ошибки равномерно распределены → Success@EOF + граница проходов.
        Assert.IsTrue(m.SuccessAtEof, $"D1.6 not recovered to EOF\n{ReportLine(m)}");
        Assert.IsTrue(m.Passes <= 2 * errorCount, $"D1.6 passes={m.Passes} > {2 * errorCount}\n{ReportLine(m)}");
    }

    [TestMethod]
    public void Test_D1_7_DoubleDamage()
    {
        var m = Measure("D1.7", NewDoubleDamageParser, GenDoubleDamage());
        Trace.WriteLine(ReportLine(m));

        // D1.7 — приёмочный тест волны 5 (A5-1): T1 не проходит (следующая конструкция битая), S3 останавливается слабо.
        // В волне 0 это baseline: Success@EOF достижим через S2/S3 (assert); resync-позиция (старт битой
        // конструкции) отчитана в ReportLine для сравнения после волны 5.
        Assert.IsTrue(m.SuccessAtEof, $"D1.7 not recovered to EOF\n{ReportLine(m)}");
    }

    // ============ D2-ядро: отчёт по всему корпусу (7 строк) ============

    [TestMethod]
    public void Test_D1_Corpus_Report()
    {
        var scenarios = new[]
        {
            Measure("D1.1", NewMiniCParser, GenManyErrors(120)),
            Measure("D1.2", NewMiniCParser, GenOneErrorAtEnd(500)),
            Measure("D1.3", NewNestedLoopParser, GenNestedLoopError()),
            Measure("D1.4", NewRegexParser, GenRegexError()),
            Measure("D1.5", NewLongestMatchParser, GenLongestMatchError()),
            Measure("D1.6", NewMiniCParser, GenDirtyFile(50, 5)),
            Measure("D1.7", NewDoubleDamageParser, GenDoubleDamage()),
        };

        var report = string.Join(Environment.NewLine, scenarios.Select(ReportLine));
        Trace.WriteLine("=== D1 Corpus Report (Wave 0 baseline) ===");
        Trace.WriteLine(report);
        Trace.WriteLine("==========================================");

        // Отчёт — измерительная база, не должен падать; позже волны меряются против этих чисел.
        Assert.AreEqual(7, scenarios.Length);
    }
}
#endif
