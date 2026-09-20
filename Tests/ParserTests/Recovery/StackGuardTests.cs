#nullable enable

using ExtensibleParser;

namespace MiniC;

// Репро 3.0a: stack overflow во время recovery re-parse.
// Вход: хвостовой мусор в истинном EOF (`int foo() { return 0; } ###`) + MaxRecoveryAttemptsPerPosition = 11.
// Механизм: S3 (терминатор=EOF) ставит абсорбер `###`→`int` в (24,int) → ре-парсинг даёт e 24→27(=EOF)
// (принят) → на итерации e=EOF генерируются кандидаты → бесконечная рекурсия ParseSeq→ParseSeq →
// переполнение стека (до фикса). После фикса guard глубины рекурсии абортит ветку (Failure) и Parse
// возвращает результат, а не роняет test host.
// Грамматика — копия MiniCEndToEndTests (Terminals — сгенерированные).
[TestClass]
public sealed class StackGuardTests
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

    // ============ 3.0a: хвостовой мусор в истинном EOF + бюджет 11 → не краш (stack guard) ============
    // После 3.0c (абсорбер на уровне цикла) хвостовой мусор в истинном EOF восстанавливается до
    // Success@EOF (S3 panic-терминатор = EOF, абсорбер глотает хвост целиком). Главное для 3.0a —
    // что Parse ВЕРНУЛ результат (Success@EOF), а не уронил test host (Stack overflow).

    [TestMethod]
    public void Test_TrailingGarbageAtEof_NoStackOverflow()
    {
        _parser.MaxRecoveryAttemptsPerPosition = 11;
        var input = "int foo() { return 0; } ###";
        var result = _parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == input.Length,
            $"Expected Success@EOF (recovered, no crash), got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} ErrorInfo={_parser.ErrorInfo?.Pos}");
        Assert.IsNull(_parser.ErrorInfo);
    }
}
