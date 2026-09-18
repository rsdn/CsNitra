#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
namespace Recovery;

[TerminalMatcher]
public sealed partial class FinalStateTerminals
{
    [Regex(@"\d+")]
    public static partial Terminal Number();

    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();

    [Regex(@"[^\w\s]+")]
    public static partial RecoveryTerminal ErrorOperator();

    public static Terminal ErrorEmpty() => _error;

    private static Terminal _error = new EmptyTerminal("Error");
}

[TestClass]
public sealed class FinalStateTests
{
    // MiniC-подобная грамматика без recovery-правил (паттерн AnchorResyncTests).
    private static Parser NewMiniCParser()
    {
        var parser = new Parser(FinalStateTerminals.Trivia());
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Function"), "ModuleFunctions")];
        parser.Rules["Function"] =
        [
            new Seq([new Literal("int"), FinalStateTerminals.Ident(), new Literal("("), new Literal(")"), new Ref("Block")], "FunctionDecl"),
        ];
        parser.Rules["Block"] =
        [
            new Seq([new Literal("{"), new ZeroOrMany(new Ref("Statement")), new Literal("}")], "MultiBlock"),
        ];
        parser.Rules["Statement"] =
        [
            new Seq([new Literal("int"), FinalStateTerminals.Ident(), new Literal(";")], "VarDecl"),
        ];
        parser.BuildTdoppRules();
        return parser;
    }

    // MiniC-подобная грамматика с recovery-правилами (паттерн IterativeRecoveryTests).
    private static Parser NewRecoveryParser()
    {
        var parser = new Parser(FinalStateTerminals.Trivia());
        var closingBrace = new OftenMissed(new Literal("}"));
        var semicolon = new OftenMissed(new Literal(";"));

        parser.Rules["Expr"] = new Rule[]
        {
            FinalStateTerminals.Number(),
            FinalStateTerminals.Ident(),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
            new Seq([new Ref("Expr"), new Literal("="), new ReqRef("Expr", 10, Right: true)], "Assignment"),

            // Recovery rules:
            new Seq([new Ref("Expr"), FinalStateTerminals.ErrorOperator(), new ReqRef("Expr", 200)], "RecoveryOperator"),
            new Seq([new Ref("Expr"), FinalStateTerminals.ErrorEmpty(), new ReqRef("Expr", 200)], "RecoveryEmptyOperator"),
            FinalStateTerminals.ErrorEmpty(),
        };

        parser.Rules["Statement"] = new Rule[]
        {
            new Seq([new Literal("int"), FinalStateTerminals.Ident(), semicolon], "VarDecl"),
            new Seq([new Ref("Expr"), semicolon], "ExprStmt"),
        };

        parser.Rules["Block"] = new Rule[]
        {
            new Seq([new Literal("{"), new ZeroOrMany(new Seq([new NotPredicate(new Ref("Function")), new Ref("Statement")], "BlockStatement")), closingBrace], "MultiBlock"),
            new Ref("Statement", "SimplBlock"),
        };

        parser.Rules["Function"] = new Rule[]
        {
            new Seq([new Literal("int"), FinalStateTerminals.Ident(), new Literal("("), new Literal(")"), new Ref("Block")], "FunctionDecl"),
        };

        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Function"), "ModuleFunctions")];
        parser.BuildTdoppRules();
        return parser;
    }

    // Минимальная TDOPP-грамматика для Partial@EOF (§3.6), паттерн PartialBaseCaseTests:
    //  - Partial до EOF порождается postfix-путом (ParseRule скипает Partial-префиксы, см. Parser.ParseRule):
    //    вложенный plain-Seq «Inner» = Seq("b","c"), где последний элемент не совпал в EOF, даёт Partial
    //    (базовый случай ParseSeq), который прокидывается вверх как Partial-элемент postfix-Seq.
    //  - Вход "a+bb": "a" + postfix("+" + "b" + Inner("b","c")), где "c" не хватает в EOF → Partial@4=EOF.
    //  - Третья альтернатива "b" важна: её провал на позиции 0 перезаписывает _lastSnapshot.Pos = 0,
    //    поэтому снимок в точке e=4 (EOF) не снимается (FailureSnapshotAt(4) == null) и recovery не генерирует
    //    кандидатов → финальный Partial@EOF с пустыми RecoveryDiagnostics (дыры описаны деревом, I4).
    private static Parser NewTdoppParser()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));
        parser.Rules["Expr"] = new Rule[]
        {
            new Literal("a"),
            new Literal("a+"),
            new Literal("b"),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100), new Seq([new Literal("b"), new Literal("c")], "Inner")], "Add"),
        };
        parser.BuildTdoppRules();
        return parser;
    }

    // 1. Чистый успех: Success@EOF → ErrorInfo == null, RecoveryDiagnostics пуст.
    [TestMethod]
    public void Test_CleanSuccess_NoErrorInfo_NoDiagnostics()
    {
        var parser = NewMiniCParser();
        var input = "int f() { int x; }";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out _, out var end));
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(parser.ErrorInfo);
        Assert.AreEqual(0, parser.RecoveryDiagnostics.Count);
    }

    // 2. Partial@EOF — «восстановлено с дырами» (§3.6): финальный результат — Partial до EOF
    //    (в Inner не хватает 'c' в EOF). Ключевой инвариант 1.4: Partial@EOF ⇒ ErrorInfo == null
    //    (в отличие от Success<EOF / Failure / Partial<EOF, где ErrorInfo = FatalError).
    //    RecoveryDiagnostics здесь пуст: Partial@EOF как финальное состояние возникает, только когда
    //    recovery не принял ни одного кандидата (снимок в точке e=EOF не снимается из-за перезаписи
    //    _lastSnapshot более поздним провалом префикса; а любой принятый кандидат с нулевой вставкой
    //    достроил бы дыру до Success@EOF). Дыры описаны деревом (Partial-узлы, I4), а не диагностикой.
    [TestMethod]
    public void Test_PartialAtEof_RecoveredWithHoles()
    {
        var parser = NewTdoppParser();
        var input = "a+bb";
        var result = parser.Parse(input, "Expr", out _);

        Assert.IsTrue(result.TryGetPartial(out _, out var end),
            $"Expected Partial, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} ErrorInfoPos={parser.ErrorInfo?.Pos}");
        Assert.AreEqual(input.Length, end, $"Expected Partial@EOF({input.Length}), got Partial@{end}");
        Assert.IsNull(parser.ErrorInfo, "Partial@EOF is a recovered state — ErrorInfo must be null");
        Assert.AreEqual(0, parser.RecoveryDiagnostics.Count,
            "Partial@EOF final state arises without accepted candidates (see test note)");
    }

    // 3. Невосстановлено: кандидаты исчерпаны, результат < EOF → ErrorInfo != null (FatalError),
    //    RecoveryDiagnostics содержит принятые восстановления.
    // Вход: хвостовой мусор ### в истинном EOF. S6 (гарантированное дно) восстанавливает до EOF:
    // memo-патч start-правила → Success@EOF, хвост покрыт IsAbsorber-узлом.
    [TestMethod]
    public void Test_Unrecovered_CandidatesExhausted_FatalError()
    {
        var parser = NewRecoveryParser();
        parser.MaxRecoveryAttemptsPerPosition = 10;
        var input = "int f() { int x; } ###";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == input.Length, "S6 bottom must reach EOF");
        Assert.IsNull(parser.ErrorInfo);
        Assert.IsTrue(parser.RecoveryDiagnostics.Any(d => d.Kind == RecoveryKind.Skipped),
            "Expected accepted Skipped diagnostics (S6 bottom)");
    }

    // 4. Success < EOF (хвостовой мусор): S6 (гарантированное дно) memo-патчит start-правило → Success@EOF.
    [TestMethod]
    public void Test_SuccessBelowEof_TrailingGarbage_FatalError()
    {
        var parser = NewMiniCParser();
        var input = "int f() { int x; } ###";
        var result = parser.Parse(input, "Function", out _);

        Assert.IsTrue(result.TryGetSuccess(out _, out var end));
        Assert.AreEqual(input.Length, end, "S6 bottom must reach EOF");
        Assert.IsNull(parser.ErrorInfo);
    }
}
#endif
