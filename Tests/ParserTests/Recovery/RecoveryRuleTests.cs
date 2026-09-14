#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
namespace Recovery;

[TestClass]
public sealed class RecoveryRuleTests
{
    // Каноническая структурная форма дерева (Kind/позиции/IsRecovery/дети) для сравнения идентичности.
    private static string Shape(ISyntaxNode node) => node switch
    {
        TerminalNode t => $"T[{t.Kind},{t.StartPos},{t.EndPos},{t.ContentLength},{t.IsRecovery}]",
        SeqNode s => $"S[{s.Kind},{s.StartPos},{s.EndPos}]({string.Join(",", s.RawElements.Select(Shape))})",
        ListNode l => $"L[{l.Kind},{l.StartPos},{l.EndPos},{l.HasTrailingSeparator},{l.IsRecovery}]({string.Join(",", l.RawElements.Select(Shape))}|{string.Join(",", l.Delimiters.Select(Shape))})",
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

    // ============ 5. MaxSkip: авторский лимит ограничивает resync-скан (S2) ============

    // MiniC-подобная грамматика: Module = ZeroOrMany(Function) → выведенный якорь Function;
    // Block = { Statement* } → выведенный якорь Statement; Foo = MARK — авторский якорь
    // (не выводится: не элемент цикла). MaxRecoveryIterations=0: кандидаты генерируются вручную.
    private static Parser NewMiniCParser()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Function"), "ModuleFunctions")];
        parser.Rules["Function"] =
        [
            new Seq([new Literal("int"), RecoveryTerminals.Ident(), new Literal("("), new Literal(")"), new Ref("Block")], "FunctionDecl"),
        ];
        parser.Rules["Block"] =
        [
            new Seq([new Literal("{"), new ZeroOrMany(new Ref("Statement")), new Literal("}")], "MultiBlock"),
        ];
        parser.Rules["Statement"] =
        [
            new Seq([new Literal("int"), RecoveryTerminals.Ident(), new Literal(";")], "VarDecl"),
        ];
        parser.Rules["Foo"] = [new Literal("MARK")];
        parser.MaxRecoveryIterations = 0;
        parser.BuildTdoppRules();
        return parser;
    }

    [TestMethod]
    public void Test_MaxSkip_Limits_Resync_Scan()
    {
        var parser = NewMiniCParser();
        var input = "int foo() { int x " + new string('#', 40) + " int bar() { int y; }";
        parser.Parse(input, "Module", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;

        // Без лимита: выведенный якорь Function (bar) в пределах дефолтного MaxSkip → есть S2-кандидат.
        var noLimit = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure);
        Assert.IsTrue(noLimit.Any(x => x.Rank == 2), "expected an S2 resync candidate without a MaxSkip limit");

        // Малый MaxSkip (кадр Module): якорь bar() дальше лимита → S2-кандидата нет.
        var stack = (StackFrame[])snapshot.Stack.Clone();
        stack[0] = stack[0] with { Options = new RecoveryOptions { MaxSkip = 5 } };
        var limited = snapshot with { Stack = stack };
        var withLimit = RecoveryEngine.Generate(e, limited, input, parser, Result.Kind.Failure);
        Assert.IsFalse(withLimit.Any(x => x.Rank == 2), "S2 resync must not reach an anchor beyond MaxSkip");
    }

    // ============ 6. Anchors: авторские расширяют выведенные (T1) ============

    [TestMethod]
    public void Test_Anchor_Author_Extends_Derived()
    {
        var parser = NewMiniCParser();
        var input = "int foo() { int x ### MARK int bar() { int y; }";
        parser.Parse(input, "Module", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;

        // Базово: только выведенные якоря → resync к `int bar()` (Function).
        var baseline = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure);
        var baseC = baseline.Single(x => x.Rank == 2);
        Assert.AreEqual("Function", baseC.TerminalKind, "baseline resync should use the derived Function anchor");

        // Авторский якорь Foo (= MARK) расширяет выведенные: resync к MARK — ближе, чем к bar().
        var stack = (StackFrame[])snapshot.Stack.Clone();
        stack[0] = stack[0] with { Options = new RecoveryOptions { Anchors = [new Ref("Foo")] } };
        var withAuthor = snapshot with { Stack = stack };
        var authorC = RecoveryEngine.Generate(e, withAuthor, input, parser, Result.Kind.Failure).Single(x => x.Rank == 2);
        Assert.AreEqual("Foo", authorC.TerminalKind, "author anchor Foo should extend the derived anchors and win by proximity");
        Assert.IsTrue(authorC.Pos < baseC.Pos, "author-anchor resync point should be closer than the derived one");
    }

    // ============ 7. Recoverable=false (opt-out) ============

    // MiniC-подобная грамматика с Error-правилом (ε), обёрнутым в RecoveryRule(Recoverable: recoverable).
    // ε-принятие срабатывает только в recovery-позиции (isRecoveryPos), т.е. нужен recovery-цикл.
    private static Parser NewOperandGrammar(bool recoverable)
    {
        var parser = new Parser(RecoveryTerminals.Trivia());
        var semicolon = new OftenMissed(new Literal(";"));
        var closingBrace = new OftenMissed(new Literal("}"));
        parser.Rules["Expr"] = new Rule[]
        {
            RecoveryTerminals.Number(),
            RecoveryTerminals.Ident(),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
            new Seq([new Ref("Expr"), new Literal("="), new ReqRef("Expr", 10, Right: true)], "Assign"),
            new RecoveryRule(RecoveryTerminals.ErrorEmpty(), new RecoveryOptions { Recoverable = recoverable }),
        };
        parser.Rules["Statement"] = new Rule[]
        {
            new Seq([new Literal("int"), RecoveryTerminals.Ident(), semicolon], "VarDecl"),
            new Seq([new Ref("Expr"), semicolon], "ExprStmt"),
        };
        parser.Rules["Block"] = new Rule[]
        {
            new Seq([new Literal("{"), new ZeroOrMany(new Seq([new NotPredicate(new Ref("Function")), new Ref("Statement")], "BlockStatement")), closingBrace], "MultiBlock"),
            new Ref("Statement", "SimplBlock"),
        };
        parser.Rules["Function"] = new Rule[]
        {
            new Seq([new Literal("int"), RecoveryTerminals.Ident(), new Literal("("), new Literal(")"), new Ref("Block")], "FunctionDecl"),
        };
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Function"), "ModuleFunctions")];
        parser.BuildTdoppRules();
        return parser;
    }

    // (а) ε-принятие выключено: вход, восстанавливавшийся через RecoveryRule-ε, больше не восстанавливается.
    [TestMethod]
    public void Test_Recoverable_False_Disables_Epsilon_Acceptance()
    {
        const string input = "int f() { z = x + ; }";

        var ok = NewOperandGrammar(recoverable: true).Parse(input, "Module", out _);
        Assert.IsTrue(ok.TryGetSuccess(out _, out var endOk) && endOk == input.Length,
            "Recoverable=true should recover the missing operand via the ε-match");

        var strict = NewOperandGrammar(recoverable: false).Parse(input, "Module", out _);
        Assert.IsFalse(strict.TryGetSuccess(out _, out var endStrict) && endStrict == input.Length,
            "Recoverable=false must not accept the ε-match (the missing operand is not recovered)");
    }

    // (б) опции кадра с Recoverable=false игнорируются engine'ом: TryInsert не генерирует кандидата.
    [TestMethod]
    public void Test_Recoverable_False_Options_Ignored_By_Engine()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));
        parser.MaxRecoveryIterations = 0;
        parser.Rules["Start"] = [new Seq([new Literal("a"), new Literal("b")], "Start")];
        parser.BuildTdoppRules();
        parser.Parse("a", "Start", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;
        var top = snapshot.Stack[^1];

        // Базово: Recoverable=true (по умолчанию) + TryInsert=c → rank-0 кандидат на c.
        var recoverableFrame = top with { Options = new RecoveryOptions { TryInsert = [new Literal("c")] } };
        var recoverableSnapshot = snapshot with { Stack = snapshot.Stack[..^1].Concat([recoverableFrame]).ToArray() };
        var withRecoverable = RecoveryEngine.Generate(e, recoverableSnapshot, "a", parser, Result.Kind.Failure);
        Assert.IsTrue(withRecoverable.Any(x => x.TerminalKind == "c" && x.Rank == 0),
            "baseline: Recoverable=true should generate a rank-0 TryInsert candidate for c");

        // Recoverable=false: тот же TryInsert, но engine опции кадра игнорирует → кандидата на c нет.
        var strictFrame = top with { Options = new RecoveryOptions { Recoverable = false, TryInsert = [new Literal("c")] } };
        var strictSnapshot = snapshot with { Stack = snapshot.Stack[..^1].Concat([strictFrame]).ToArray() };
        var withStrict = RecoveryEngine.Generate(e, strictSnapshot, "a", parser, Result.Kind.Failure);
        Assert.IsFalse(withStrict.Any(x => x.TerminalKind == "c"),
            "Recoverable=false: the engine must ignore the frame's TryInsert options");
    }
}
#endif
