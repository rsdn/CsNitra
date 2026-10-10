#nullable enable

using System.Diagnostics;
using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class V2IterationLimitScaleTests
{
    private static Parser NewParser()
    {
        var parser = new Parser(A8Terminals.Trivia());
        parser.Rules["Statement"] = [new Seq([new Literal("int"), A8Terminals.Ident(), new Literal(";")], "VarDecl")];
        parser.Rules["Block"] = [new Seq([new Literal("{"), new ZeroOrMany(new Ref("Statement"), "Statements"), new Literal("}")], "BlockBody")];
        parser.Rules["Function"] = [new Seq([new Literal("int"), A8Terminals.Ident(), new Literal("("), new Literal(")"), new Ref("Block")], "FunctionDecl")];
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Function"), "Functions")];
        parser.BuildTdoppRules();
        return parser;
    }

    private static string BuildInput(int n)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < n; i++)
            sb.Append($"int f{i}() {{ int a{i} int b{i}; }}");
        return sb.ToString();
    }

    [TestMethod]
    [TestCategory("Probe")]
    [Ignore("Manual diagnostic: heavy; not part of the normal suite")]
    public void Test_IterationLimit_Scale_N1200()
    {
        var parser = NewParser();
        parser.Profile = RecoveryProfile.Ide;
        var input = BuildInput(1200);
        var stopwatch = Stopwatch.StartNew();
        var result = parser.Parse(input, "Module", out _);
        stopwatch.Stop();

        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == input.Length, "V2: iteration limit at scale must still force Success@EOF");
        Assert.IsNull(parser.ErrorInfo, "V2: no FatalError on forced bottom");
        Assert.IsTrue(parser.RecoveryDiagnostics.Any(d => d.Kind == RecoveryKind.Unrecovered), "V2: Unrecovered diagnostic expected at scale");

        Trace.WriteLine($"V2 scale N=1200: elapsed={stopwatch.ElapsedMilliseconds} ms, RecoveryPasses={parser.RecoveryPasses}, EngineGenerateCalls={parser.EngineGenerateCalls}");
    }
}
