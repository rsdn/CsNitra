#nullable enable

using Calc;
using Diagnostics;
using ExtensibleParser;
using Tests.Extensions;

namespace NoRecoverySmoke;

// Additive smoke test for the single-pass parser path: uses only core rule types
// (Seq, Literal, Terminal) and must pass without relying on recovery. The old
// #if RECOVERY / -p:EnableRecovery build split was removed in A4-5 (single build).
[TestClass]
public sealed class NoRecoverySmokeTests
{
    private readonly Parser _parser = new(Terminals.Trivia(), new Log(LogImportance.Non));

    public NoRecoverySmokeTests()
    {
        _parser.Rules["Stmt"] = new Rule[]
        {
            new Seq([new Literal("int"), Terminals.Number()], Kind: "IntDecl"),
        };

        _parser.BuildTdoppRules();
    }

    [TestMethod]
    public void ValidInput_SucceedsAndConsumesToEof()
    {
        const string input = "int 7";

        var result = _parser.Parse(input, "Stmt", out _);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.TryGetSuccess(out _, out var endPos));
        Assert.AreEqual(input.Length, endPos);
        Assert.IsNull(_parser.ErrorInfo);
    }

    [TestMethod]
    public void InvalidInput_FailsWithErrorInfoAtExpectedPosition()
    {
        const string input = "x";

        var result = _parser.Parse(input, "Stmt", out _);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, _parser.ErrorPos);

        var errorInfo = _parser.ErrorInfo.AssertIsNonNull();
        Assert.AreEqual(0, errorInfo.Pos);
        CollectionAssert.AreEqual(new[] { "int" }, errorInfo.Expecteds.Select(t => t.Kind).ToArray());
    }
}
