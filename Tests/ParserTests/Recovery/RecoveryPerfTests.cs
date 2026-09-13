#nullable enable

using System.Diagnostics;
using System.Text;
using ExtensibleParser;

#if RECOVERY
namespace Recovery;

[TerminalMatcher]
public sealed partial class PerfTerminals
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

// Бенчмарк (2.4): N ошибок → число проходов, время, размер memo. МиниC-подобная грамматика
// (по образцу IterativeRecoveryTests): N функций, в каждой пропущена ; между двумя var-decl.
// Recovery восстанавливает вход до Success@EOF; префикс кэшируется в memo, поэтому время/memo
// масштабируются сублинейно (не пропорционально N).
[TestClass]
public sealed class RecoveryPerfTests
{
    private static Parser NewParser()
    {
        var parser = new Parser(PerfTerminals.Trivia());
        var closingBrace = new OftenMissed(new Literal("}"));
        var semicolon = new OftenMissed(new Literal(";"));

        parser.Rules["Expr"] = new Rule[]
        {
            PerfTerminals.Number(),
            PerfTerminals.Ident(),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
            new Seq([new Ref("Expr"), new Literal("="), new ReqRef("Expr", 10, Right: true)], "Assignment"),

            // Recovery rules:
            new Seq([new Ref("Expr"), PerfTerminals.ErrorOperator(), new ReqRef("Expr", 200)], "RecoveryOperator"),
            new Seq([new Ref("Expr"), PerfTerminals.ErrorEmpty(), new ReqRef("Expr", 200)], "RecoveryEmptyOperator"),
            PerfTerminals.ErrorEmpty(),
        };

        parser.Rules["Statement"] = new Rule[]
        {
            new Seq([new Literal("int"), PerfTerminals.Ident(), semicolon], "VarDecl"),
            new Seq([new Ref("Expr"), semicolon], "ExprStmt"),
        };

        parser.Rules["Block"] = new Rule[]
        {
            new Seq([new Literal("{"), new ZeroOrMany(new Seq([new NotPredicate(new Ref("Function")), new Ref("Statement")], "BlockStatement")), closingBrace], "MultiBlock"),
            new Ref("Statement", "SimplBlock"),
        };

        parser.Rules["Function"] = new Rule[]
        {
            new Seq([new Literal("int"), PerfTerminals.Ident(), new Literal("("), new Literal(")"), new Ref("Block")], "FunctionDecl"),
        };

        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Function"), "ModuleFunctions")];

        parser.BuildTdoppRules();
        return parser;
    }

    // N функций, в каждой пропущена ; между int a{i} и int b{i} (N ошибок).
    private static string GenerateInput(int n)
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= n; i++)
            sb.Append($"int f{i}() {{ int a{i} int b{i}; }}");
        return sb.ToString();
    }

    private sealed record Measurement(int N, double MinTimeMs, int Passes, int MemoCount, bool SuccessAtEof, int GenCalls);

    // Минимум из K прогонов (снижает шум тайминга); Passes/MemoCount/GenCalls детерминированы (последний прогон).
    private static Measurement Measure(int n, int runs = 5)
    {
        var input = GenerateInput(n);
        var minTime = double.MaxValue;
        var passes = 0;
        var memoCount = 0;
        var successAtEof = false;
        var genCalls = 0;

        for (var r = 0; r < runs; r++)
        {
            var parser = NewParser();
            var sw = Stopwatch.StartNew();
            var result = parser.Parse(input, "Module", out _);
            sw.Stop();
            minTime = Math.Min(minTime, sw.Elapsed.TotalMilliseconds);
            passes = parser.RecoveryPasses;
            memoCount = parser.Memo.Count;
            genCalls = parser.EngineGenerateCalls;
            successAtEof = result.TryGetSuccess(out _, out var end) && end == input.Length;
        }

        return new Measurement(n, minTime, passes, memoCount, successAtEof, genCalls);
    }

    [TestMethod]
    public void Test_Recovery_Performance_Scaling()
    {
        var m1 = Measure(1);
        var m5 = Measure(5);
        var m10 = Measure(10);
        var m20 = Measure(20);

        var report = string.Join(Environment.NewLine, new[]
        {
            $"N=1:  time={m1.MinTimeMs:F3}ms passes={m1.Passes} memo={m1.MemoCount} gen={m1.GenCalls} success@EOF={m1.SuccessAtEof}",
            $"N=5:  time={m5.MinTimeMs:F3}ms passes={m5.Passes} memo={m5.MemoCount} gen={m5.GenCalls} success@EOF={m5.SuccessAtEof}",
            $"N=10: time={m10.MinTimeMs:F3}ms passes={m10.Passes} memo={m10.MemoCount} gen={m10.GenCalls} success@EOF={m10.SuccessAtEof}",
            $"N=20: time={m20.MinTimeMs:F3}ms passes={m20.Passes} memo={m20.MemoCount} gen={m20.GenCalls} success@EOF={m20.SuccessAtEof}",
        });
        Trace.WriteLine(report);

        // (а) все N ошибок восстановлены (Success@EOF).
        Assert.IsTrue(m1.SuccessAtEof, $"N=1 not recovered to EOF:\n{report}");
        Assert.IsTrue(m5.SuccessAtEof, $"N=5 not recovered to EOF:\n{report}");
        Assert.IsTrue(m10.SuccessAtEof, $"N=10 not recovered to EOF:\n{report}");
        Assert.IsTrue(m20.SuccessAtEof, $"N=20 not recovered to EOF:\n{report}");

        // (б) RecoveryPasses ограничен: каждая итерация чинит ≥1 ошибку → ≤ 2N (с запасом).
        Assert.IsTrue(m1.Passes <= 2 * 1, $"N=1 passes={m1.Passes} > 2\n{report}");
        Assert.IsTrue(m5.Passes <= 2 * 5, $"N=5 passes={m5.Passes} > 10\n{report}");
        Assert.IsTrue(m10.Passes <= 2 * 10, $"N=10 passes={m10.Passes} > 20\n{report}");
        Assert.IsTrue(m20.Passes <= 2 * 20, $"N=20 passes={m20.Passes} > 40\n{report}");

        // (в) масштабирование времени: время(N=10) < 3 × время(N=5) (префикс кэшируется в memo).
        Assert.IsTrue(m10.MinTimeMs < 3.0 * m5.MinTimeMs,
            $"time(N=10)={m10.MinTimeMs:F3}ms not < 3*time(N=5)={3.0 * m5.MinTimeMs:F3}ms\n{report}");

        // (г) масштабирование memo: Memo.Count(N=10) < 3 × Memo.Count(N=5) (префикс кэшируется).
        Assert.IsTrue(m10.MemoCount < 3 * m5.MemoCount,
            $"memo(N=10)={m10.MemoCount} not < 3*memo(N=5)={3 * m5.MemoCount}\n{report}");
    }
}
#endif
