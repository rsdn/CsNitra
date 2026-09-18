#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
namespace Recovery;

[TerminalMatcher]
public sealed partial class AnchorTerminals
{
    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

[TestClass]
public sealed class AnchorResyncTests
{
    // MiniC-подобная грамматика (по образцу MiniCTests):
    // Module = ZeroOrMany(Function); Function = int ident ( ) Block; Block = { Statement* }; Statement = int ident ;
    private static Parser NewParser()
    {
        var parser = new Parser(AnchorTerminals.Trivia());
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Function"), "ModuleFunctions")];
        parser.Rules["Function"] =
        [
            new Seq([new Literal("int"), AnchorTerminals.Ident(), new Literal("("), new Literal(")"), new Ref("Block")], "FunctionDecl"),
        ];
        parser.Rules["Block"] =
        [
            new Seq([new Literal("{"), new ZeroOrMany(new Ref("Statement")), new Literal("}")], "MultiBlock"),
        ];
        parser.Rules["Statement"] =
        [
            new Seq([new Literal("int"), AnchorTerminals.Ident(), new Literal(";")], "VarDecl"),
        ];
        parser.Rules["MemberStart"] =
        [
            new Seq([new Literal("int"), AnchorTerminals.Ident()], "MemberStart"),
        ];
        parser.MaxRecoveryIterations = 0;
        parser.BuildTdoppRules();
        return parser;
    }

    // Пример 1 §3.4: пропущенная } перед следующей функцией.
    // T1-якорь Function (выведен из ZeroOrMany(Function) в Module) спекулятивно совпадает в S == e:
    // completion stack — вставки ; (VarDecl) и } (Block) в e, cost 2.
    [TestMethod]
    public void Test_T1_Resync_At_Error_Pos_Missing_Closing_Brace()
    {
        var parser = NewParser();
        var input = "int foo() { int x\nint bar() { int y; }";
        parser.Parse(input, "Module", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;
        Assert.AreEqual(18, e);

        var candidates = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure, "Start", 0, e);

        var c = candidates.Single(x => x.Rank == 2);
        Assert.AreEqual("S2:Statement:T1:18", c.Id);
        Assert.AreEqual(18, c.Pos);
        Assert.AreEqual(2, c.Cost);
        Assert.AreEqual("Function", c.TerminalKind);
        Assert.AreEqual(RecoveryKind.Inserted, c.Diagnostics[0].Kind);

        c.Apply(parser);
        Assert.IsTrue(parser.Injections.TryGetValue((e, new Literal(";")), out var semi));
        Assert.AreEqual(0, semi.Length);
        Assert.IsFalse(semi.IsSkip);
        Assert.IsTrue(parser.Injections.TryGetValue((e, new Literal("}")), out var brace));
        Assert.AreEqual(0, brace.Length);
        Assert.IsFalse(brace.IsSkip);

        c.Rollback(parser);
        Assert.IsFalse(parser.Injections.ContainsKey((e, new Literal(";"))));
        Assert.IsFalse(parser.Injections.ContainsKey((e, new Literal("}"))));
    }

    // Мусор между e и якорем: S > e, completion stack содержит абсорбер [e..S).
    [TestMethod]
    public void Test_T1_Garbage_Between_E_And_Anchor_Absorber()
    {
        var parser = NewParser();
        var input = "int foo() { int x\n### int bar() { int y; }";
        parser.Parse(input, "Module", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;
        Assert.AreEqual(18, e);

        var candidates = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure, "Start", 0, e);

        var c = candidates.Single(x => x.Rank == 2);
        Assert.AreEqual("S2:Statement:T1:22", c.Id);
        Assert.AreEqual(22, c.Pos);
        Assert.AreEqual(3, c.Cost);
        Assert.AreEqual(RecoveryKind.Skipped, c.Diagnostics[0].Kind);
        Assert.AreEqual(e, c.Diagnostics[0].StartPos);
        Assert.AreEqual(22, c.Diagnostics[0].EndPos);

        c.Apply(parser);
        Assert.IsTrue(parser.Injections.TryGetValue((e, new Literal(";")), out var absorber));
        Assert.AreEqual(4, absorber.Length);
        Assert.IsTrue(absorber.IsSkip);
        Assert.IsTrue(parser.Injections.TryGetValue((22, new Literal("}")), out var brace));
        Assert.AreEqual(0, brace.Length);

        c.Rollback(parser);
        Assert.IsFalse(parser.Injections.ContainsKey((e, new Literal(";"))));
        Assert.IsFalse(parser.Injections.ContainsKey((22, new Literal("}"))));
    }

    // Пример 2 §3.4: двойная ошибка. T1 в S == e не срабатывает (bar бит),
    // T2 (авторский CanStart = MemberStart) срабатывает → кандидат T2 (cost +1).
    [TestMethod]
    public void Test_T2_CanStart_Double_Error()
    {
        var parser = NewParser();
        var input = "int foo() { int x\nint bar( { int y; }";
        parser.Parse(input, "Module", out _);
        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;
        Assert.AreEqual(18, e);

        // CanStart задаётся через Options кадра снимка (Module — ближайший кадр с записанным полем).
        var stack = (StackFrame[])snapshot.Stack.Clone();
        stack[0] = stack[0] with { Options = new RecoveryOptions { CanStart = [new Ref("MemberStart")] } };
        var rebuilt = snapshot with { Stack = stack };

        var candidates = RecoveryEngine.Generate(e, rebuilt, input, parser, Result.Kind.Failure, "Start", 0, e);

        var t2 = candidates.Single(x => x.Id == "S2:Statement:T2:18");
        Assert.AreEqual(18, t2.Pos);
        Assert.AreEqual(3, t2.Cost);
        Assert.AreEqual("MemberStart", t2.TerminalKind);

        // Скан после T2 продолжается и находит T1 в S == 29 (int y; внутри тела bar).
        // Cost = skip([18..29): 3 слова «int», «bar(», «{») + 2 вставки + 0 (T1).
        var t1 = candidates.Single(x => x.Id == "S2:Statement:T1:29");
        Assert.AreEqual(29, t1.Pos);
        Assert.AreEqual(5, t1.Cost);

        // Порядок: (Rank, Cost) — T2 (3) перед T1 (6).
        var s2 = candidates.Where(x => x.Rank == 2).ToList();
        Assert.AreEqual("S2:Statement:T2:18", s2[0].Id);
        Assert.AreEqual("S2:Statement:T1:29", s2[1].Id);
    }
}

#endif
