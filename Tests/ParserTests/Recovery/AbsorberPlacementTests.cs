#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;
using System.Text;

#if RECOVERY
namespace MiniC;

// 3.0c: размещение абсорбера S2/S3 на уровне цикла/Seq.
// Мусор МЕЖДУ итерациями цикла (между функциями) глотается целиком абсорбером, который патчит
// memo правила-элемента цикла (Function) в точке e — следующая итерация стартует с чистого
// терминала. Хвостовой мусор в истинном EOF (S3, терминатор = EOF) — аналогично: абсорбер
// глотает хвост целиком, разбор доходит до Success@EOF, абсорбер представлен в дереве (IsRecovery).
// До фикса: абсорбер ставился слепо на упавший терминал верхнего кадра (Literal("int") в Function),
// и Ident съедал настоящий ключ `int` следующей функции → рассинхрон (between: ErrorInfo=36, bar
// не разобран; true-EOF: не Success@EOF). Грамматика — копия MiniCEndToEndTests (Terminals —
// сгенерированные). Бюджет ставится ЯВНО (дефолт 3 — предохранитель, см. 3.0b).
[TestClass]
public sealed class AbsorberPlacementTests
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

    // ============ 3.0c: мусор МЕЖДУ функциями → S2 resync на уровне цикла → Success@EOF ============

    [TestMethod]
    public void Test_BetweenFunctions_AbsorberAtLoopLevel_SuccessAtEof()
    {
        _parser.MaxRecoveryAttemptsPerPosition = 16;
        var input = "int foo() { return 0; } ### int bar() { return 1; }";
        var result = _parser.Parse(input, "Module", out _);

        // Абсорбер [24..28) глотает `### ` целиком на уровне цикла (Function), bar разбирается целиком.
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"Expected Success@EOF (bar parsed whole), got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} " +
            $"ErrorInfo={_parser.ErrorInfo?.Pos} passes={_parser.RecoveryPasses} diag={Describe(_parser.RecoveryDiagnostics)} tree={DescribeTree(node)}");
        Assert.IsNull(_parser.ErrorInfo);

        // Skipped-диагностика абсорбера (S2 resync).
        var skipped = _parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Skipped).ToList();
        Assert.IsTrue(skipped.Count >= 1,
            $"Expected >= 1 Skipped diagnostic (absorber), got {skipped.Count}. All: {Describe(_parser.RecoveryDiagnostics)}");

        // Абсорбер представлен в дереве (IsRecovery-узел).
        var recoveryNodes = CostCalculator.CountRecoveryNodes(node);
        Assert.IsTrue(recoveryNodes >= 1,
            $"Expected >= 1 recovery node (absorber), got {recoveryNodes}. tree={DescribeTree(node)}");
    }

    // ============ 3.0c: хвостовой мусор в истинном EOF → S3 на уровне цикла → Success@EOF ============

    [TestMethod]
    public void Test_TrueEof_AbsorberAtLoopLevel_SuccessAtEof()
    {
        _parser.MaxRecoveryAttemptsPerPosition = 16;
        var input = "int foo() { return 0; } ###";
        var result = _parser.Parse(input, "Module", out _);

        // Абсорбер [24..27) глотает хвост `###` целиком на уровне цикла (Function) → Success@EOF.
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"Expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} " +
            $"ErrorInfo={_parser.ErrorInfo?.Pos} passes={_parser.RecoveryPasses} diag={Describe(_parser.RecoveryDiagnostics)} tree={DescribeTree(node)}");
        Assert.IsNull(_parser.ErrorInfo);

        // Skipped-диагностика абсорбера (S3 panic, терминатор = EOF).
        var skipped = _parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Skipped).ToList();
        Assert.IsTrue(skipped.Count >= 1,
            $"Expected >= 1 Skipped diagnostic (absorber), got {skipped.Count}. All: {Describe(_parser.RecoveryDiagnostics)}");

        // Абсорбер представлен в дереве (IsRecovery-узел).
        var recoveryNodes = CostCalculator.CountRecoveryNodes(node);
        Assert.IsTrue(recoveryNodes >= 1,
            $"Expected >= 1 recovery node (absorber), got {recoveryNodes}. tree={DescribeTree(node)}");
    }

    // ============ Хелперы ============

    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) term={d.Terminal?.Kind ?? "-"} rule={d.RuleName ?? "-"}"));

    private static string DescribeTree(ISyntaxNode node)
    {
        var sb = new StringBuilder();
        DescribeTree(node, sb);
        return sb.ToString();
    }

    private static void DescribeTree(ISyntaxNode node, StringBuilder sb)
    {
        var content = node is TerminalNode t ? $" len={t.ContentLength}" : "";
        var rec = node.IsRecovery ? " [REC]" : "";
        sb.Append($"{node.Kind}[{node.StartPos}-{node.EndPos}){content}{rec} ");
        switch (node)
        {
            case SeqNode seq:
                foreach (var el in seq.RawElements)
                    DescribeTree(el, sb);
                break;
            case ListNode list:
                foreach (var el in list.RawElements)
                    DescribeTree(el, sb);
                foreach (var d in list.Delimiters)
                    DescribeTree(d, sb);
                break;
            case SomeNode some:
                DescribeTree(some.Value, sb);
                break;
        }
    }
}
#endif
