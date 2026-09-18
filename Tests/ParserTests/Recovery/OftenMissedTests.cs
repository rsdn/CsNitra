#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
namespace Recovery;

// 2.3: OftenMissed — документированный сахар над TryInsert: эквивалент
// RecoveryRule(Element, new RecoveryOptions(TryInsert: [Element])). Кадр элемента/альтернативы
// несёт опцию TryInsert, поэтому engine (S1) генерирует кандидата вставки терминала в точке
// восстановления e — поведение совместимо с инлайновой вставкой в recovery-позиции.
[TestClass]
public sealed class OftenMissedTests
{
    // MiniC-подобная грамматика (паттерн S0IntegrationTests): OftenMissed(";") в VarDecl/ExprStmt,
    // OftenMissed("}") в MultiBlock.
    private static Parser NewMiniCParser()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());
        var semicolon = new OftenMissed(new Literal(";"));
        var closingBrace = new OftenMissed(new Literal("}"));

        parser.Rules["Expr"] = new Rule[]
        {
            RecoveryTerminals.Number(),
            RecoveryTerminals.Ident(),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
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

    // Минимальная грамматика: S = a ;? — сбой в позиции OftenMissed-элемента (точка e),
    // recovery-цикл отключён (кандидаты генерируются вручную).
    private static Parser NewMinimalParser()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());
        parser.MaxRecoveryIterations = 0;
        parser.Rules["S"] = [new Seq([new Literal("a"), new OftenMissed(new Literal(";"))], "S")];
        parser.BuildTdoppRules();
        return parser;
    }

    // ============ 1. Эквивалентность (поведение v1): OftenMissed в recovery-позиции вставляет
    //    пропущенный терминал — вход восстанавливается до Success@EOF, в дереве recovery-узел ============

    [TestMethod]
    public void Test_OftenMissed_Inserts_Missing_Terminal_At_RecoveryPos()
    {
        var parser = NewMiniCParser();
        var input = "int f() { int x int y; }";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end));
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(parser.ErrorInfo);
        Assert.IsTrue(CostCalculator.CountRecoveryNodes((Node)node) >= 1,
            "expected a recovery node for the inserted ';' in the tree");
    }

    // ============ 2. Механизм (подход А): кадр OftenMissed-элемента несёт Options.TryInsert = [элемент] ============

    [TestMethod]
    public void Test_OftenMissed_Frame_Carries_TryInsert()
    {
        var parser = NewMinimalParser();
        var input = "a";
        parser.Parse(input, "S", out _);

        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;
        Assert.AreEqual(input.Length, e);

        var top = snapshot.Stack[^1];
        Assert.IsTrue(top.Location is SeqFrameLocation, $"expected a Seq element frame on top, got: {top.Location}");
        Assert.IsNotNull(top.Options, "expected the OftenMissed element frame to carry RecoveryOptions");
        Assert.IsTrue(
            top.Options!.TryInsert is [var t] && TerminalComparer.Instance.Equals(t, new Literal(";")),
            $"expected TryInsert = [';'], got: [{string.Join(", ", (top.Options.TryInsert ?? Array.Empty<Terminal>()).Select(x => x.Kind))}]");
    }

    // ============ 3. Engine видит TryInsert: кандидат вставки OftenMissed-терминала в точке e,
    //    диагностика Inserted; Apply → инъекция (e, ';'), Rollback → удалена ============

    [TestMethod]
    public void Test_OftenMissed_TryInsert_Candidate_Generated_At_E()
    {
        var parser = NewMinimalParser();
        var input = "a";
        parser.Parse(input, "S", out _);

        var snapshot = parser.LastSnapshot;
        Assert.IsNotNull(snapshot);
        var e = snapshot!.Pos;

        var candidates = RecoveryEngine.Generate(e, snapshot, input, parser, Result.Kind.Failure, "Start", 0, e);

        var c = candidates.Single(x => x.TerminalKind == ";" && x.Pos == e);
        Assert.AreEqual(RecoveryKind.Inserted, c.Diagnostics[0].Kind);
        Assert.AreEqual(e, c.Diagnostics[0].StartPos);

        c.Apply(parser);
        Assert.IsTrue(parser.Injections.TryGetValue((e, new Literal(";")), out var inj));
        Assert.AreEqual(0, inj.Length);
        Assert.IsFalse(inj.IsSkip);

        c.Rollback(parser);
        Assert.IsFalse(parser.Injections.ContainsKey((e, new Literal(";"))));
    }
}
#endif
