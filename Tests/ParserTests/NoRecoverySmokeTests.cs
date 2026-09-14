#nullable enable

using Calc;
using Diagnostics;
using ExtensibleParser;
using Tests.Extensions;

namespace NoRecoverySmoke;

// Additive smoke test for the pure single-pass (non-recovery) parser build
// (-p:EnableRecovery=false). NOT gated with #if RECOVERY: compiles and must
// pass in both modes, using only core rule types (Seq, Literal, Terminal).
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
