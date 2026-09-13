#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
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
    public void Test_RecoveryDiagnostics_Empty_AfterFailure()
    {
        var parser = NewParser();
        var result = parser.Parse("ax", "Start", out _);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(parser.RecoveryDiagnostics);
        Assert.AreEqual(0, parser.RecoveryDiagnostics.Count);
    }
}

#endif
