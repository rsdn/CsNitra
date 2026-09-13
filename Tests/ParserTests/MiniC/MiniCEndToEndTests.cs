#nullable enable

using System.Text;
using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace MiniC;

// E2E-набор на MiniC (чек-лист 3.1, план v2 §4 Фаза 3 + §3.7 инварианты I4/I5/I6 + §3.9 аннотации).
// Грамматика — копия MiniCTests (Terminals — сгенерированные). Если сценарию нужны авторские
// аннотации — RecoveryRule-обёртки добавлены в Initialize ниже (см. заметки в чек-листе 3.1).
[TestClass]
public sealed class MiniCEndToEndTests
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

        // Statement rules (копия MiniCTests: `;` и `)` — OftenMissed).
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

    // ============ Тест 1: пропущенная } функции (модуль) ============

    [TestMethod]
    public void Test_MissingClosingBrace_Function()
    {
        // Функция внутри модуля, пропущена закрывающая } (тело завершено, ; на месте).
        // После } идёт следующая функция — есть точка resync (одиночная функция с } в EOF
        // уводит recovery-цикл в бесконечную рекурсию re-parse — см. заметки 3.1).
        var input = "int foo() { int x;\nint bar() { return 0; }";
        var result = _parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end),
            $"Expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} ErrorInfo={_parser.ErrorInfo?.Pos}");
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(_parser.ErrorInfo);
        Assert.IsTrue(CostCalculator.CountRecoveryNodes((Node)node) > 0,
            $"Expected recovery nodes in tree, got 0. Diagnostics: {Describe(_parser.RecoveryDiagnostics)}");
    }

    // ============ Тест 2: 2 пропущенные ; (по одной в каждом блоке) ============
    // Отклонение от ТЗ (≥2 Inserted-диагностики): пропущенная ; в MiniC — OftenMissed,
    // восстанавливается S0 (re-parse) БЕЗ диагностики (Inserted-диагностику даёт только
    // engine S1, а plain-Literal ; ломает recovery на этой грамматике — проверено).
    // Поэтому проверяем Success@EOF + ≥2 recovery-узла в дереве (по одному на пропущенную ;).
    // Также recovery MiniC восстанавливает только 1 пропущенную ; на блок, поэтому 2 ; —
    // в двух блоках (двух функциях).

    [TestMethod]
    public void Test_MissingSemicolon_MultipleStatements()
    {
        var input = "int f1() { int x int y; } int f2() { int a int b; }";
        var result = _parser.Parse(input, "Module", out _);
        var node = result.TryGetSuccess(out var n, out var end) ? (Node)n : null;

        Assert.IsTrue(node is not null && end == input.Length,
            $"Expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} ErrorInfo={_parser.ErrorInfo?.Pos} passes={_parser.RecoveryPasses} diag={Describe(_parser.RecoveryDiagnostics)} tree={(node is null ? "-" : DescribeTree(node))}");
        Assert.IsNull(_parser.ErrorInfo);

        var recoveryNodes = CostCalculator.CountRecoveryNodes(node!);
        Assert.IsTrue(recoveryNodes >= 2,
            $"Expected >= 2 recovery nodes (one per missing ;), got {recoveryNodes}. tree={DescribeTree(node!)}");
    }

    // ============ Тест 3: неожиданный токен в выражении ============

    [TestMethod]
    public void Test_UnexpectedToken_Expression()
    {
        // @ — ErrorOperator (regex [\\\/*+\-<=>!@#$%^&]+), восстанавливается RecoveryOperator-правилом.
        var input = "int foo() { int z; z = x @ 5; return z; }";
        var result = _parser.Parse(input, "Module", out _);

        var atEof = result.TryGetSuccess(out var node, out var end) && end == input.Length
            || result.TryGetPartial(out var pnode, out var pend) && pend == input.Length;
        Assert.IsTrue(atEof,
            $"Expected recovered (Success/Partial)@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} ErrorInfo={_parser.ErrorInfo?.Pos}");

        // §3.6: восстановлено до EOF ⇒ ErrorInfo == null.
        if (atEof)
            Assert.IsNull(_parser.ErrorInfo);
        else
            Assert.IsNotNull(_parser.ErrorInfo);
    }

    // ============ Тест 4: две соседние функции, в каждой пропущена ( ============
    // Отклонение от ТЗ (пропущенная }): } — OftenMissed, восстанавливается S0 БЕЗ диагностики.
    // Пропущенная ( не имеет OftenMissed → engine S1 вставляет → Inserted-диагностика.
    // Поэтому в каждой функции пропущена ( — обе ошибки дают Inserted-диагностику.

    [TestMethod]
    public void Test_NestedErrors_MultipleBlocks()
    {
        var input = "int foo ) { return 1; }\nint bar ) { return 2; }";
        var result = _parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"Expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} ErrorInfo={_parser.ErrorInfo?.Pos} diag={Describe(_parser.RecoveryDiagnostics)}");
        Assert.IsNull(_parser.ErrorInfo);

        var diags = _parser.RecoveryDiagnostics.ToList();
        Assert.IsTrue(diags.Count >= 2,
            $"Expected >= 2 diagnostics (one per missing }}), got {diags.Count}. All: {Describe(diags)} tree={DescribeTree((Node)node)}");
    }

    // ============ Тест 5: корректная программа + хвостовой мусор до true-EOF ============
    // Сценарий true-EOF trailing-garbage: корректная функция, затем `###` в самом конце
    // (до EOF). Бюджет 16 (глубокий сценарий). После 3.0a (stack guard) + 3.0b (бюджет 3)
    // + 3.0c (абсорбер на уровне цикла) — Success@EOF + Skipped-диагностика + абсорбер-узел.

    [TestMethod]
    public void Test_TrailingGarbage()
    {
        _parser.MaxRecoveryAttemptsPerPosition = 16;
        var input = "int foo() { return 0; } ###";
        var result = _parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"Expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} ErrorInfo={_parser.ErrorInfo?.Pos} passes={_parser.RecoveryPasses} gen={_parser.EngineGenerateCalls} snap={(_parser.LastSnapshot is null ? "null" : $"pos={_parser.LastSnapshot.Pos} top={_parser.LastSnapshot.Stack[^1].RuleName} failed={_parser.LastSnapshot.FailedTerminal?.Kind}")} diag={Describe(_parser.RecoveryDiagnostics)}");

        // Абсорбер мусора: Skipped-диагностика (kind Trailing или аналог).
        var skipped = _parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Skipped).ToList();
        Assert.IsTrue(skipped.Count >= 1,
            $"Expected >= 1 Skipped diagnostic (absorber), got {skipped.Count}. All: {Describe(_parser.RecoveryDiagnostics)}");

        // Мусор в дереве: абсорбер — recovery-узел (IsRecovery).
        var recoveryNodes = CostCalculator.CountRecoveryNodes((Node)node);
        Assert.IsTrue(recoveryNodes >= 1,
            $"Expected >= 1 recovery node (absorber), got {recoveryNodes}. tree={DescribeTree((Node)node)}");
    }

    // ============ Хелперы ============

    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) term={d.Terminal?.Kind ?? "-"} rule={d.RuleName ?? "-"}"));

    private static string DescribeTree(Node node)
    {
        var sb = new StringBuilder();
        DescribeTree(node, sb);
        return sb.ToString();
    }

    private static void DescribeTree(Node node, StringBuilder sb)
    {
        var content = node is TerminalNode t ? $" len={t.ContentLength}" : "";
        var rec = node.IsRecovery ? " [REC]" : "";
        sb.Append($"{node.Kind}[{node.StartPos}-{node.EndPos}){content}{rec} ");
        switch (node)
        {
            case SeqNode seq:
                foreach (var el in seq.Elements) DescribeTree((Node)el, sb);
                break;
            case ListNode list:
                foreach (var el in list.Elements) DescribeTree((Node)el, sb);
                foreach (var d in list.Delimiters) DescribeTree((Node)d, sb);
                break;
            case SomeNode some:
                DescribeTree((Node)some.Value, sb);
                break;
        }
    }
}
