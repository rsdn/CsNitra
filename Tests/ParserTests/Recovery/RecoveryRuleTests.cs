#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class RecoveryRuleTests
{
    // Каноническая структурная форма дерева (Kind/позиции/IsRecovery/дети) для сравнения идентичности.
    private static string Shape(ISyntaxNode node) => node switch
    {
        TerminalNode t => $"T[{t.Kind},{t.StartPos},{t.EndPos},{t.ContentLength},{t.IsRecovery}]",
        SeqNode s => $"S[{s.Kind},{s.StartPos},{s.EndPos}]({string.Join(",", s.Elements.Select(Shape))})",
        ListNode l => $"L[{l.Kind},{l.StartPos},{l.EndPos},{l.HasTrailingSeparator},{l.IsRecovery}]({string.Join(",", l.Elements.Select(Shape))}|{string.Join(",", l.Delimiters.Select(Shape))})",
        SomeNode o => $"Some[{o.Kind},{o.StartPos},{o.EndPos}]({Shape(o.Value)})",
        NoneNode n => $"None[{n.Kind},{n.StartPos},{n.EndPos}]",
        PredicateNode p => $"P[{p.Kind},{p.StartPos},{p.EndPos}]",
        _ => $"N[{node.Kind},{node.StartPos},{node.EndPos},{node.IsRecovery}]"
    };

    private static ISyntaxNode? NodeOf(Result r)
    {
        if (r.TryGetSuccess(out var n, out _))
            return n;
        if (r.TryGetPartial(out n, out _))
            return n;
        return null;
    }

    // Грамматика S = a X c, X = x y. wrap: false = без обёртки, true = X обёрнут в RecoveryRule.
    private static Parser BuildGrammar(bool wrap, RecoveryOptions? options)
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));
        parser.Rules["S"] = [new Seq([new Literal("a"), new Ref("X"), new Literal("c")], "S")];
        var x = new Seq([new Literal("x"), new Literal("y")], "X");
        parser.Rules["X"] = wrap
            ? [new RecoveryRule(x, options)]
            : [x];
        parser.BuildTdoppRules();
        return parser;
    }

    // ============ 1. Развёртка: дерево/результат идентичны Inner (корректный и некорректный вход) ============

    [TestMethod]
    public void ParsesLikeInner()
    {
        var baseline = BuildGrammar(wrap: false, null);
        var withOptions = BuildGrammar(wrap: true, new RecoveryOptions { TryInsert = [new Literal(";")], Terminators = [new Literal("c")] });
        var withoutOptions = BuildGrammar(wrap: true, null);

        var inputs = new[] { "axyc", "axc" }; // корректный и некорректный (не хватает y)

        foreach (var input in inputs)
        {
            var rb = baseline.Parse(input, "S", out _);
            var rw = withOptions.Parse(input, "S", out _);
            var rn = withoutOptions.Parse(input, "S", out _);

            Assert.AreEqual(rb.ResultKind, rw.ResultKind, $"kind mismatch (with options) for «{input}»");
            Assert.AreEqual(rb.ResultKind, rn.ResultKind, $"kind mismatch (no options) for «{input}»");
            Assert.AreEqual(rb.NewPos, rw.NewPos, $"newPos mismatch (with options) for «{input}»");
            Assert.AreEqual(rb.NewPos, rn.NewPos, $"newPos mismatch (no options) for «{input}»");

            var nb = NodeOf(rb);
            var nw = NodeOf(rw);
            var nn = NodeOf(rn);
            Assert.AreEqual(nb is null, nw is null, $"node-presence mismatch (with options) for «{input}»");
            Assert.AreEqual(nb is null, nn is null, $"node-presence mismatch (no options) for «{input}»");

            if (nb is not null)
            {
                Assert.AreEqual(Shape(nb), Shape(nw!), $"tree mismatch (with options) for «{input}»");
                Assert.AreEqual(Shape(nb), Shape(nn!), $"tree mismatch (no options) for «{input}»");
            }
        }
    }

    // ============ 2. Options попадают в кадр альтернативы (RuleFrameLocation) ============

    private sealed class StackProbe
    {
        public Parser? Parser;
        public IReadOnlyList<StackFrame>? Captured;
    }

    private sealed record ProbeTerminal(StackProbe Probe) : Terminal("probe")
    {
        public override int TryMatch(string input, int startPos)
        {
            if (Probe.Parser is { } parser)
                Probe.Captured = parser.CurrentStackFrames.ToArray();
            return input.Length > startPos && input[startPos] == 'x' ? 1 : -1;
        }
    }

    [TestMethod]
    public void Options_Land_In_AlternativeFrame()
    {
        var probe = new StackProbe();
        var probeTerminal = new ProbeTerminal(probe);
        var parser = new Parser(new EmptyTerminal("Trivia"));
        probe.Parser = parser;

        var options = new RecoveryOptions { TryInsert = [new Literal(";")], Terminators = [new Literal("c")] };
        parser.Rules["S"] = [new Seq([new Literal("a"), new Ref("X"), new Literal("c")], "S")];
        parser.Rules["X"] = [new RecoveryRule(new Seq([probeTerminal], "X"), options)];
        parser.BuildTdoppRules();

        var result = parser.Parse("axc", "S", out _);
        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(probe.Captured);

        // Кадр альтернативы X (RuleFrameLocation) несёт именно Options обёртки.
        var desc = string.Join("; ", probe.Captured!.Select(f => $"{f.RuleName}:{f.Location}:opts={(f.Options is null ? "null" : ReferenceEquals(f.Options, options) ? "match" : "other")}"));
        Assert.IsTrue(
            probe.Captured.Any(f => f.RuleName == "X" && f.Location is RuleFrameLocation && ReferenceEquals(f.Options, options)),
            $"expected an X alternative frame carrying the RecoveryRule options; captured: {desc}");
    }

    // ============ 3. RecoveryRule-префикс в правиле с TDOPP-оператором (развёртка работает) ============

    [TestMethod]
    public void Tdopp_WrappedPrefix_ParsesLikeInner()
    {
        var baseline = new Parser(new EmptyTerminal("Trivia"));
        baseline.Rules["Expr"] = [
            RecoveryTerminals.Number(),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
        ];
        baseline.BuildTdoppRules();

        var wrapped = new Parser(new EmptyTerminal("Trivia"));
        wrapped.Rules["Expr"] = [
            new RecoveryRule(RecoveryTerminals.Number(), new RecoveryOptions { TryInsert = [new Literal(";")] }),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
        ];
        wrapped.BuildTdoppRules();

        foreach (var input in new[] { "1+2+3", "42" })
        {
            var rb = baseline.Parse(input, "Expr", out _);
            var rw = wrapped.Parse(input, "Expr", out _);
            Assert.AreEqual(rb.ResultKind, rw.ResultKind, $"kind mismatch for «{input}»");
            Assert.AreEqual(rb.NewPos, rw.NewPos, $"newPos mismatch for «{input}»");
            var nb = NodeOf(rb);
            var nw = NodeOf(rw);
            Assert.AreEqual(nb is null, nw is null);
            if (nb is not null)
                Assert.AreEqual(Shape(nb), Shape(nw!), $"tree mismatch for «{input}»");
        }
    }

    // ============ 4. Обёртка не ломает инлайнинг одно-префиксных правил ============

    [TestMethod]
    public void WrappedSinglePrefixRule_ReferencedByRef()
    {
        // X — одно-префиксное правило, обёрнутое в RecoveryRule, и используется через Ref.
        var parser = new Parser(new EmptyTerminal("Trivia"));
        parser.Rules["S"] = [new Seq([new Literal("a"), new Ref("X"), new Literal("c")], "S")];
        parser.Rules["X"] = [new RecoveryRule(new Seq([new Literal("x"), new Literal("y")], "X"), new RecoveryOptions { Recoverable = false })];
        parser.BuildTdoppRules();

        var result = parser.Parse("axyc", "S", out _);
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end));
        Assert.AreEqual(4, end);
        Assert.AreEqual("S", node.Kind);
    }
}
