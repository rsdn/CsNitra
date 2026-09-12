#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class RecoveryDiagnosticTests
{
    private static Parser NewParser()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));
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
