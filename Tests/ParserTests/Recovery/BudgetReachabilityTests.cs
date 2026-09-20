#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace MiniC;

// 3.0b: достижимость S2/S3/S5 при ЯВНО выставленном бюджете (16).
// S1 генерирует до |FollowSet|+2 ≈ 9-12 кандидатов и с дефолтом 3 (и даже 10) выедает
// пер-позиционный бюджет раньше, чем цикл дойдёт до S2/S3/S5 (ранг 2/3/5):
//   between-функции: S2 = 11-я попытка; true-EOF: S3 = 11-я, S5 = 13-я.
// Дефолт 3 — «предохранитель» по плану; глубокие сценарии ставят бюджет ЯВНО (глобальный
// подъём дефолта ломает консервативное поведение существующих тестов — проверено, 3.0b).
// 3.0a (stack guard) гарантирует, что подъём бюджета не даёт краша. Грамматика — копия
// MiniCEndToEndTests (Terminals — сгенерированные). Здесь проверяем только ДОСТИЖИМОСТЬ:
// кандидат нужного ранга попробован и принят (прогресс) → его Skipped-диагностика в
// RecoveryDiagnostics. Полное восстановление до Success@EOF — задача 3.0c (абсорбер).
[TestClass]
public sealed class BudgetReachabilityTests
{
    private readonly Parser _parser = new(Terminals.Trivia());

    [TestInitialize]
    public void Initialize()
    {
        var closingParenthesis = new OftenMissed(new Literal("}"));
        var closingBracket = new OftenMissed(new Literal(")"));

        // Expression rules
        _parser.Rules["Expr"] = new Rule[]
        {
            Terminals.Number(),
            Terminals.Ident(),
            new Seq([new Literal("("), new Ref("Expr"), new Literal(")")], "Parens"),
            new Seq([Terminals.Ident(), new Literal("("), closingBracket], "CallNoArgs"),
            new Seq([Terminals.Ident(), new Literal("("), new SeparatedList(new Ref("Expr"), new Literal(","), Kind: "ArgsRest", EndBehavior: SeparatorEndBehavior.Forbidden ), closingBracket], "Call"),
            new Seq([new Literal("-"), new ReqRef("Expr", 300)], "Neg"),

            new Seq([new Ref("Expr"), new Literal("*"),  new ReqRef("Expr", 200)], "Mul"),
            new Seq([new Ref("Expr"), new Literal("/"),  new ReqRef("Expr", 200)], "Div"),
            new Seq([new Ref("Expr"), new Literal("+"),  new ReqRef("Expr", 100)], "Add"),
            new Seq([new Ref("Expr"), new Literal("-"),  new ReqRef("Expr", 100)], "Sub"),
            new Seq([new Ref("Expr"), new Literal("=="), new ReqRef("Expr",  50)], "Eq"),
            new Seq([new Ref("Expr"), new Literal("!="), new ReqRef("Expr",  50)], "Neq"),
            new Seq([new Ref("Expr"), new Literal("<"),  new ReqRef("Expr",  50)], "Lt"),
            new Seq([new Ref("Expr"), new Literal(">"),  new ReqRef("Expr",  50)], "Gt"),
            new Seq([new Ref("Expr"), new Literal("<="), new ReqRef("Expr",  50)], "Le"),
            new Seq([new Ref("Expr"), new Literal(">="), new ReqRef("Expr",  50)], "Ge"),
            new Seq([new Ref("Expr"), new Literal("&&"), new ReqRef("Expr", 30)], "And"),
            new Seq([new Ref("Expr"), new Literal("||"), new ReqRef("Expr", 20)], "Or"),
            new Seq([new Ref("Expr"), new Literal("="),  new ReqRef("Expr", 10, Right: true)], "AssignmentExpr"),

            // Recovery rules:
            new Seq([new Ref("Expr"), (Terminals.ErrorOperator()), new ReqRef("Expr",  200)], "RecoveryOperator"),
            new Seq([new Ref("Expr"), Terminals.ErrorEmpty(), new ReqRef("Expr", 200)], "RecoveryEmptyOperator"),
            new RecoveryRule(Terminals.ErrorEmpty()),
        };

        // Statement rules
        _parser.Rules["Statement"] = new Rule[]
        {
            new Seq([new Literal("int"), Terminals.Ident(), new OftenMissed(new Literal(";"))], "VarDecl"),
            new Seq([
                new Literal("int"), new Literal("["), new Literal("]"), Terminals.Ident(), new Literal("="), new Literal("{"),
                new SeparatedList(Terminals.Number(), new Literal(","), Kind: "ArrayDeclItems", EndBehavior: SeparatorEndBehavior.Optional),
                closingParenthesis, new OftenMissed(new Literal(";"))
            ], "ArrayDecl"),
            new Seq([new Ref("Expr"), new OftenMissed(new Literal(";"))], "ExprStmt"),
            new Seq([new Literal("if"), new Literal("("), new Ref("Expr"), closingBracket,
                    new Ref("Block")], "IfStmt"),
            new Seq([new Literal("if"), new Literal("("), new Ref("Expr"), closingBracket,
                    new Ref("Block"), new Literal("else"), new Ref("Block")], "IfElseStmt"),
            new Seq([new Literal("return"), new Ref("Expr"), new OftenMissed(new Literal(";"))], "Return")
        };

        // Block rules
        _parser.Rules["Block"] =
        [
            new Seq([new Literal("{"), new ZeroOrMany(new Seq([new NotPredicate(new Ref("Function")), new Ref("Statement")], "BlockStatement")), closingParenthesis], "MultiBlock"),
            new Ref("Statement", "SimplBlock")
        ];

        _parser.Rules["Params"] = [
            new SeparatedList(Terminals.Ident(), new Literal(","), Kind: "ParamsRest", EndBehavior: SeparatorEndBehavior.Forbidden, CanBeEmpty: false),
            new Literal("void", "VoidParams"),
        ];

        // Function declaration
        _parser.Rules["Function"] = [
            new Seq([
                new Literal("int"),
                Terminals.Ident(),
                new Literal("("),
                new Optional(new Ref("Params")),
                closingBracket,
                new Ref("Block")
            ], "FunctionDecl")
        ];

        _parser.Rules["Module"] = [new ZeroOrMany(new Ref("Function"), "ModuleFunctions")];

        _parser.BuildTdoppRules();
    }

    // ============ 3.0b: мусор МЕЖДУ функциями → S2 resync достижим при явном бюджете ============

    [TestMethod]
    public void Test_BetweenFunctions_S2Resync_Reachable()
    {
        // Глубокий сценарий: бюджет ставится явно (дефолт 3 — предохранитель, см. 3.0b).
        _parser.MaxRecoveryAttemptsPerPosition = 16;
        var input = "int foo() { return 0; } ### int bar() { return 1; }";
        var result = _parser.Parse(input, "Module", out _);

        // S2 (ранг 2) — 11-я попытка на e; с бюджетом 16 добирается до попытки и принимается
        // (прогресс e 24→32→36, см. декомпозицию 3.0b) → Skipped-диагностика в RecoveryDiagnostics.
        var skipped = _parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Skipped).ToList();
        Assert.IsTrue(skipped.Count >= 1,
            $"Expected >= 1 Skipped diagnostic (S2 resync reached), got {skipped.Count}. " +
            $"result={result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} " +
            $"ErrorInfo={_parser.ErrorInfo?.Pos} passes={_parser.RecoveryPasses} gen={_parser.EngineGenerateCalls} " +
            $"diag={Describe(_parser.RecoveryDiagnostics)}");
    }

    // ============ 3.0b: хвостовой мусор в истинном EOF → S3/S5 достижимы при явном бюджете ============

    [TestMethod]
    public void Test_TrueEof_S3S5_Reachable()
    {
        // Глубокий сценарий: бюджет ставится явно (дефолт 3 — предохранитель, см. 3.0b).
        _parser.MaxRecoveryAttemptsPerPosition = 16;
        var input = "int foo() { return 0; } ###";
        var result = _parser.Parse(input, "Module", out _);

        // S3 (ранг 3) — 11-я, S5 (ранг 5) — 13-я попытка на e; с бюджетом 16 добираются до попытки.
        // S3 принимается (прогресс e 24→27=EOF, см. декомпозицию 3.0b) → Skipped-диагностика.
        // (Полное восстановление до Success@EOF — задача 3.0c.)
        var skipped = _parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Skipped).ToList();
        Assert.IsTrue(skipped.Count >= 1,
            $"Expected >= 1 Skipped diagnostic (S3/S5 reached), got {skipped.Count}. " +
            $"result={result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} " +
            $"ErrorInfo={_parser.ErrorInfo?.Pos} passes={_parser.RecoveryPasses} gen={_parser.EngineGenerateCalls} " +
            $"diag={Describe(_parser.RecoveryDiagnostics)}");
    }

    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) term={d.Terminal?.Kind ?? "-"} rule={d.RuleName ?? "-"}"));
}
