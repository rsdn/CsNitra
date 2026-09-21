#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TerminalMatcher]
public sealed partial class A8Terminals
{
    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

[TestClass]
public sealed class A8IterationLimitTests
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

    private static IEnumerable<TerminalNode> FindAbsorbers(ISyntaxNode node)
    {
        switch (node)
        {
            case TerminalNode t when t.IsAbsorber:
                yield return t;
                break;
            case SeqNode seq:
                foreach (var el in seq.RawElements)
                    foreach (var a in FindAbsorbers(el))
                        yield return a;
                break;
            case ListNode list:
                foreach (var el in list.RawElements)
                    foreach (var a in FindAbsorbers(el))
                        yield return a;
                foreach (var d in list.Delimiters)
                    foreach (var a in FindAbsorbers(d))
                        yield return a;
                break;
            case SomeNode some:
                foreach (var a in FindAbsorbers(some.Value))
                    yield return a;
                break;
        }
    }

    [TestMethod]
    public void Test_IterationLimit_ForcedBottom_ToEof()
    {
        var parser = NewParser();
        parser.Profile = RecoveryProfile.Ide;
        parser.MaxRecoveryIterations = 2;
        var input = BuildInput(5);
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == input.Length, "A8: iteration limit must force Success@EOF");
        Assert.IsNull(parser.ErrorInfo, "A8: no FatalError on forced bottom");
        Assert.IsTrue(parser.RecoveryDiagnostics.Any(d => d.Kind == RecoveryKind.Unrecovered), "A8: Unrecovered diagnostic expected");
        result.TryGetSuccess(out var node, out _);
        Assert.IsTrue(node is not null);
        var absorber = FindAbsorbers(node!).Last();
        Assert.AreEqual(input.Length, absorber.EndPos, "A8: absorber must reach EOF");
    }
}
