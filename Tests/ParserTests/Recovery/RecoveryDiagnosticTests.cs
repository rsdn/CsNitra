#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class RecoveryDiagnosticTests
{
    // Recovery отключён: тест проверяет диагностику на НЕвосстановленном parse, а не цикл восстановления.
    private static Parser NewParser()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));
        parser.MaxRecoveryIterations = 0;
        parser.Rules["Start"] = [new Seq([new Literal("a"), new Literal("b")], "Start")];
        parser.BuildTdoppRules();
        return parser;
    }

    [TestMethod]
    public void Test_RecoveryDiagnostics_Empty_AfterSuccess()
    {
        var parser = NewParser();
        var result = parser.Parse("ab", "Start", out _);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(parser.RecoveryDiagnostics);
        Assert.AreEqual(0, parser.RecoveryDiagnostics.Count);
    }

    [TestMethod]
    public void Test_RecoveryDiagnostics_Unrecovered_AfterIterationLimit()
    {
        var parser = NewParser();
        var result = parser.Parse("ax", "Start", out _);

        // A8: исчерпание лимита (limit 0) — форс-дно до EOF + ровно один Unrecovered.
        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == 2,
            $"A8 limit-exhaustion must reach EOF, got {result.ResultKind}@{result.NewPos}");
        Assert.IsNull(parser.ErrorInfo, "recovered to EOF → ErrorInfo = null (A8)");
        Assert.IsNotNull(parser.RecoveryDiagnostics);
        var unrecovered = parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Unrecovered).ToList();
        Assert.AreEqual(1, unrecovered.Count, $"expected exactly 1 Unrecovered, got {parser.RecoveryDiagnostics.Count} total");
        Assert.AreEqual(1, unrecovered[0].StartPos, "Unrecovered must be at the recovery point e=1");
        Assert.AreEqual("iteration limit reached (0), absorbed to EOF", unrecovered[0].Message, "A8 limit-exhaustion message");
    }
}
