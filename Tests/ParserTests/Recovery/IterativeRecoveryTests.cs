#nullable enable

using ExtensibleParser;

namespace Recovery;

[TerminalMatcher]
public sealed partial class RecoveryTerminals
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
public sealed class IterativeRecoveryTests
{
    private readonly Parser _parser = new(RecoveryTerminals.Trivia());

    [TestInitialize]
    public void Initialize()
    {
        var closingBrace = new OftenMissed(new Literal("}"));
        var semicolon = new OftenMissed(new Literal(";"));

        _parser.Rules["Expr"] = new Rule[]
        {
            RecoveryTerminals.Number(),
            RecoveryTerminals.Ident(),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
            new Seq([new Ref("Expr"), new Literal("="), new ReqRef("Expr", 10, Right: true)], "Assignment"),

            // Recovery rules:
            new Seq([new Ref("Expr"), RecoveryTerminals.ErrorOperator(), new ReqRef("Expr", 200)], "RecoveryOperator"),
            new Seq([new Ref("Expr"), RecoveryTerminals.ErrorEmpty(), new ReqRef("Expr", 200)], "RecoveryEmptyOperator"),
            RecoveryTerminals.ErrorEmpty(),
        };

        _parser.Rules["Statement"] = new Rule[]
        {
            new Seq([new Literal("int"), RecoveryTerminals.Ident(), semicolon], "VarDecl"),
            new Seq([new Ref("Expr"), semicolon], "ExprStmt"),
        };

        _parser.Rules["Block"] = new Rule[]
        {
            new Seq([new Literal("{"), new ZeroOrMany(new Seq([new NotPredicate(new Ref("Function")), new Ref("Statement")], "BlockStatement")), closingBrace], "MultiBlock"),
            new Ref("Statement", "SimplBlock"),
        };

        _parser.Rules["Function"] = new Rule[]
        {
            new Seq([new Literal("int"), RecoveryTerminals.Ident(), new Literal("("), new Literal(")"), new Ref("Block")], "FunctionDecl"),
        };

        _parser.Rules["Module"] = [new ZeroOrMany(new Ref("Function"), "ModuleFunctions")];

        _parser.BuildTdoppRules();
    }

    // ============ 1. Один проход на одну ошибку: пропущенная ; ============

    [TestMethod]
    public void Test_MissingSemicolon_RecoveredInOnePass()
    {
        var input = "int f() { int x int y; }";
        var result = _parser.Parse(input, "Module", out _);
        Assert.IsTrue(result.TryGetSuccess(out _, out var end));
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(_parser.ErrorInfo);
    }

    // ============ 2. Пропущенная } ============

    [TestMethod]
    public void Test_MissingClosingBrace_RecoveredInOnePass()
    {
        var input = "int f() { int x; int y;\nint g() { int z; }";
        var result = _parser.Parse(input, "Module", out _);
        Assert.IsTrue(result.TryGetSuccess(out _, out var end));
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(_parser.ErrorInfo);
    }

    // ============ 3. Лимит итераций: recovery двигает E вперёд, но до EOF не доходит ============

    [TestMethod]
    public void Test_MaxRecoveryIterations_StopsWithoutHanging()
    {
        // Пять ошибок оператора: каждая итерация восстанавливает одну и двигает E вперёд;
        // при лимите 4 итераций последняя остаётся невосстановленной.
        var input = "int f() { z = x $^ 5; z = a $^ 6; z = b $^ 7; z = c $^ 8; z = d $^ 9; }";
        _parser.MaxRecoveryIterations = 4;
        var result = _parser.Parse(input, "Module", out _);
        Assert.IsFalse(result.TryGetSuccess(out _, out var end) && end == input.Length);
        Assert.IsNotNull(_parser.ErrorInfo);
    }

    [TestMethod]
    public void Test_ZeroIterations_StopsImmediately()
    {
        var input = "int f() { int x int y; }";
        _parser.MaxRecoveryIterations = 0;
        var result = _parser.Parse(input, "Module", out _);
        Assert.IsFalse(result.TryGetSuccess(out _, out var end) && end == input.Length);
        Assert.IsNotNull(_parser.ErrorInfo);
    }

    // ============ 4. Корректный код без recovery ============

    [TestMethod]
    public void Test_ValidInput_NoRecovery()
    {
        var input = "int f() { int x; int y; }";
        var result = _parser.Parse(input, "Module", out _);
        Assert.IsTrue(result.TryGetSuccess(out _, out var end));
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(_parser.ErrorInfo);
        Assert.AreEqual(0, _parser.RecoveryDiagnostics.Count);
    }

    // ============ 5. Несколько ошибок: прогресс E по итерациям до EOF ============

    [TestMethod]
    public void Test_MultipleErrors_MultiPassRecovery()
    {
        var input = "int f() { z = x $^ 5; z = a $^ 6; z = b $^ 7; z = c $^ 8; z = d $^ 9; }";
        var result = _parser.Parse(input, "Module", out _);
        Assert.IsTrue(result.TryGetSuccess(out _, out var end));
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(_parser.ErrorInfo);
    }

    // ============ 6. Ленивый Generate: S0 восстанавливает без Generate, при нехватке S0 — Generate вызывается ============

    [TestMethod]
    public void Test_LazyGenerate_Counter()
    {
        // Пропущенная ; восстанавливается S0 (Hygiene re-parse) за одну итерацию — Generate не вызывается.
        var missingSemicolon = "int f() { int x int y; }";
        var r1 = _parser.Parse(missingSemicolon, "Module", out _);
        Assert.IsTrue(r1.TryGetSuccess(out _, out var end1));
        Assert.AreEqual(missingSemicolon.Length, end1);
        Assert.AreEqual(0, _parser.EngineGenerateCalls);

        // Пропущенная ( (нет OftenMissed для неё): S0 не восстанавливает — Generate вызывается (>= 1, вставка S1).
        var missingParen = "int f ) { int x; }";
        var r2 = _parser.Parse(missingParen, "Module", out _);
        Assert.IsTrue(r2.TryGetSuccess(out _, out var end2));
        Assert.AreEqual(missingParen.Length, end2);
        Assert.IsTrue(_parser.EngineGenerateCalls >= 1, $"Expected EngineGenerateCalls >= 1, got {_parser.EngineGenerateCalls}");
    }
}
