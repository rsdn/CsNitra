#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// Интеграция Fазы 1: MiniC-подобная грамматика с Error-правилами (RecoveryPrefix),
// где вход с пропущенным терминалом/операндом восстанавливается через S0/S1/ε-совпадение,
// а RecoveryDiagnostics корректны.
[TestClass]
public sealed class S0IntegrationTests
{
    private readonly Parser _parser = new(RecoveryTerminals.Trivia());

    [TestInitialize]
    public void Initialize()
    {
        var semicolon = new OftenMissed(new Literal(";"));
        var closingBrace = new OftenMissed(new Literal("}"));

        _parser.Rules["Expr"] = new Rule[]
        {
            RecoveryTerminals.Number(),
            RecoveryTerminals.Ident(),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
            new Seq([new Ref("Expr"), new Literal("="), new ReqRef("Expr", 10, Right: true)], "Assign"),
            // Error-правило (RecoveryPrefix): ε-совпадение пропущенного операнда, аннотировано RecoveryRule.
            new RecoveryRule(RecoveryTerminals.ErrorEmpty()),
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

    // ============ 1. Пропущенная ; (S0 / OftenMissed): Success@EOF, невосстановленной ошибки нет ============

    [TestMethod]
    public void MissingSemicolon_RecoveredByS0()
    {
        var input = "int f() { int x int y; }";
        var result = _parser.Parse(input, "Module", out _);
        Assert.IsTrue(result.TryGetSuccess(out _, out var end));
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(_parser.ErrorInfo);
    }

    // ============ 2. Пропущенная ( (нет OftenMissed): S1-вставка → Inserted-диагностика ============

    [TestMethod]
    public void MissingParen_RecoveredByS1_HasInsertedDiagnostic()
    {
        var input = "int f ) { int x; }";
        var result = _parser.Parse(input, "Module", out _);
        Assert.IsTrue(result.TryGetSuccess(out _, out var end));
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(_parser.ErrorInfo);

        var inserted = _parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Inserted).ToList();
        Assert.IsTrue(inserted.Count >= 1,
            $"expected >= 1 Inserted diagnostic, got: {Describe(_parser.RecoveryDiagnostics)}");
        Assert.IsTrue(inserted.Any(d => d.Terminal?.Kind == "("),
            $"expected an Inserted '(' diagnostic, got: {Describe(_parser.RecoveryDiagnostics)}");
    }

    // ============ 3. Пропущенный операнд (ε-совпадение RecoveryRule): Success@EOF ============

    [TestMethod]
    public void MissingOperand_RecoveredByRecoveryRule()
    {
        var input = "int f() { z = x + ; }";
        var result = _parser.Parse(input, "Module", out _);
        Assert.IsTrue(result.TryGetSuccess(out _, out var end));
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(_parser.ErrorInfo);
    }

    // ============ 4. Корректный код: без recovery-узлов, diag пуст (I6) ============

    [TestMethod]
    public void ValidInput_NoDiagnostics()
    {
        var input = "int f() { int x; z = x + 1; }";
        var result = _parser.Parse(input, "Module", out _);
        Assert.IsTrue(result.TryGetSuccess(out _, out var end));
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(_parser.ErrorInfo);
        Assert.AreEqual(0, _parser.RecoveryDiagnostics.Count);
    }

    private static string Describe(System.Collections.Generic.IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) term={d.Terminal?.Kind ?? "-"} {d.Message}"));
}
