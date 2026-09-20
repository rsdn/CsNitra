#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace MiniC;

[TestClass]
public sealed class MiniCReferenceTests
{
    private readonly Parser _parser = new(Terminals.Trivia());

    [TestInitialize]
    public void Initialize() => MiniCGrammar.ConfigureRules(_parser);

    public const string Program = """
        int f01()
        {
            return 0;
        }

        int f02(x)
        {
            return x;
        }

        int f03(x, y)
        {
            return x + y;
        }

        int f04(x, y, z)
        {
            return x - y - z;
        }

        int f05(x, y, z)
        {
            return x * y / z;
        }

        int f06(x, y, z)
        {
            return x < y && y <= z;
        }

        int f07(x, y, z)
        {
            return x > y || y >= z;
        }

        int f08(x, y, z)
        {
            return x == y != z;
        }

        int f09(x, y)
        {
            return -(x + y) * 2;
        }

        int f10()
        {
            return f01();
        }

        int f11(x)
        {
            return f01(x);
        }

        int f12(x, y, z)
        {
            return f01(x, y, z);
        }

        int f13(x, y)
        {
            return f02(x + y);
        }

        int f14(x)
        {
            int a;
            int b;
            a = b = 5;
            b = x + a;
            return a;
        }

        int f15(x)
        {
            if (x > 0)
                x = 1;
            return x;
        }

        int f16(x)
        {
            int y;
            if (x > 0)
            {
                y = 1;
            }
            else
            {
                y = 0;
            }
            return y;
        }

        int f17()
        {
            int[] a = { 1, 2, 3 };
            int[] b = { };
            return 42;
        }

        int f18(x, y, z)
        {
            return ((x + y) * (z - 1)) / 2;
        }

        int f19(x, y, z)
        {
            int a;
            int b;
            int[] t = { 1, 2 };
            a = x * 2 + y;
            b = z - 1;
            if (a > b)
            {
                a = b;
            }
            else
            {
                b = a;
            }
            if (a == b)
                a = f02(a);
            return a + b;
        }

        int f20(x, y, z)
        {
            int a;
            int b;
            int c;
            int[] v = { 1, 2, 3, 4 };
            a = x + y;
            b = y - z;
            c = a * b / 2;
            if (c > 0 && a >= 1)
            {
                c = c - 1;
            }
            else if (c < 0 || a <= 0)
            {
                c = c + 1;
            }
            a = f03(x, y, z);
            b = f12(a, b, c);
            if (a == b)
                a = 1;
            else
                a = 0;
            return c;
        }
    """;

    [TestMethod]
    public void ReferenceProgram_MatchesExpectedAst()
    {
        var result = _parser.Parse(Program, "Module", out _);
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end));
        Assert.AreEqual(Program.Length, end);
        Assert.IsNull(_parser.ErrorInfo);

        var visitor = new MiniCTests.MiniCVisitor(Program);
        node.Accept(visitor);
        Assert.IsNotNull(visitor.Result);

        Assert.IsTrue(MatchModule(visitor.Result), $"Module AST mismatch:\n{visitor.Result}");
    }

    // ============ Тесты с ошибками: повреждённая функция + неизменность остальных ============

    // Хвостовой мусор: абсорбер глотает «###», модуль остаётся идентичным референсному.
    // Бюджет ставится ЯВНО (дефолт 3 — предохранитель): S3-абсорбер хвоста — глубокий кандидат (11-я+ попытка).
    [TestMethod]
    public void Err_TrailingGarbage_ModuleAstUnchanged()
    {
        _parser.MaxRecoveryAttemptsPerPosition = 16;
        var input = Program + " ###";
        var result = _parser.Parse(input, "Module", out _);
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end));
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(_parser.ErrorInfo);

        var visitor = new MiniCTests.MiniCVisitor(input);
        node.Accept(visitor);
        Assert.IsNotNull(visitor.Result);
        Assert.IsTrue(MatchModule(visitor.Result), $"Module AST must stay reference-identical:\n{visitor.Result}");

        Assert.IsTrue(_parser.RecoveryDiagnostics.Any(d => d.Kind == RecoveryKind.Skipped));
        Assert.IsTrue(CostCalculator.CountRecoveryNodes(node) >= 1);
    }

    // Пропущенная «;» после VarDecl в f14: нулевая вставка (OftenMissed), visitor её не видит — модуль идентичен референсному.
    [TestMethod]
    public void Err_MissingSemicolon_ModuleAstUnchanged()
    {
        var input = Program.Replace("int a;\n        int b;\n        a = b = 5;", "int a\n        int b;\n        a = b = 5;");
        var result = _parser.Parse(input, "Module", out _);
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end));
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(_parser.ErrorInfo);

        var visitor = new MiniCTests.MiniCVisitor(input);
        node.Accept(visitor);
        Assert.IsNotNull(visitor.Result);
        Assert.IsTrue(MatchModule(visitor.Result), $"Module AST must stay reference-identical:\n{visitor.Result}");

        Assert.IsTrue(CostCalculator.CountRecoveryNodes(node) >= 1);
    }

    // f05: неожиданный оператор «$» вместо «*» — повреждена только f05, остальной модуль идентичен референсному.
    // RecoveryOperator имеет приоритет 200 (как * /) и левую ассоциативность: «x $ y / z» = «(x $ y) / z».
    [TestMethod]
    public void Err_UnexpectedOperator_OnlyF05Damaged()
    {
        var input = Program.Replace("return x * y / z;", "return x $ y / z;");
        var module = ParseModule(input, nameof(Err_UnexpectedOperator_OnlyF05Damaged));
        var damaged = (Ast f) => f is FunctionDecl
        {
            Name: Identifier("f05"),
            Parameters: [Identifier("x"), Identifier("y"), Identifier("z")],
            Body: Block
            {
                HasBraces: true,
                Statements:
                [
                    ReturnStmt
                {
                    Value: BinaryExpr
                    {
                        Op: "/",
                        Left: BinaryExpr { Op: "«Unexpected: $»", Left: Identifier("x"), Right: Identifier("y") },
                        Right: Identifier("z")
                    }
                }
                ]
            }
        };
        Assert.IsTrue(MatchModuleExcept(4, module, damaged), $"Module must match reference except damaged f05:\n{module}");
    }

    // f03: пропущенный оператор между x и y — повреждена только f03, остальной модуль идентичен референсному.
    [TestMethod]
    public void Err_MissingOperator_OnlyF03Damaged()
    {
        var input = Program.Replace("return x + y;", "return x y;");
        var module = ParseModule(input, nameof(Err_MissingOperator_OnlyF03Damaged));
        var damaged = (Ast f) => f is FunctionDecl
        {
            Name: Identifier("f03"),
            Parameters: [Identifier("x"), Identifier("y")],
            Body: Block
            {
                HasBraces: true,
                Statements: [ReturnStmt { Value: BinaryExpr { Op: "«Missing operator»", Left: Identifier("x"), Right: Identifier("y") } }]
            }
        };
        Assert.IsTrue(MatchModuleExcept(2, module, damaged), $"Module must match reference except damaged f03:\n{module}");
    }

    // f10: пропущенная «}» — нулевая вставка, маркер ошибки в конце блока f10; остальные функции идентичны референсному.
    [TestMethod]
    public void Err_MissingClosingBrace_OnlyF10Damaged()
    {
        var input = Program.Replace("        return f01();\n    }", "        return f01();");
        var module = ParseModule(input, nameof(Err_MissingClosingBrace_OnlyF10Damaged));
        var damaged = (Ast f) => f is FunctionDecl
        {
            Name: Identifier("f10"),
            Parameters: [],
            Body: Block
            {
                HasBraces: true,
                Statements:
                [
                    ReturnStmt { Value: CallExpr { Name: Identifier("f01"), Arguments: Args { Arguments: [] } } },
                    Error
                ]
            }
        };
        Assert.IsTrue(MatchModuleExcept(9, module, damaged), $"Module must match reference except damaged f10:\n{module}");
    }

    private Ast ParseModule(string input, string testName)
    {
        var result = _parser.Parse(input, "Module", out _);
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end), $"Parse failed in {testName}");
        Assert.AreEqual(input.Length, end, $"Parse did not reach EOF in {testName}");
        Assert.IsNull(_parser.ErrorInfo, $"Parser reported error in {testName}");

        var visitor = new MiniCTests.MiniCVisitor(input);
        node.Accept(visitor);
        Assert.IsNotNull(visitor.Result, $"AST is null in {testName}");
        return visitor.Result!;
    }

    // Модуль идентичен референсному, кроме функции damagedIndex (проверяется damaged).
    private static bool MatchModuleExcept(int damagedIndex, Ast? actual, Func<Ast, bool> damaged)
    {
        if (actual is not Block { HasBraces: false, Statements: var statements } || statements.Count != 20)
            return false;
        for (var i = 0; i < 20; i++)
        {
            var match = i == damagedIndex ? damaged(statements[i]) : MatchFunction(i, statements[i]);
            if (!match)
                return false;
        }
        return true;
    }

    private static bool MatchFunction(int index, Ast ast) => index switch
    {
        0 => MatchF01(ast),
        1 => MatchF02(ast),
        2 => MatchF03(ast),
        3 => MatchF04(ast),
        4 => MatchF05(ast),
        5 => MatchF06(ast),
        6 => MatchF07(ast),
        7 => MatchF08(ast),
        8 => MatchF09(ast),
        9 => MatchF10(ast),
        10 => MatchF11(ast),
        11 => MatchF12(ast),
        12 => MatchF13(ast),
        13 => MatchF14(ast),
        14 => MatchF15(ast),
        15 => MatchF16(ast),
        16 => MatchF17(ast),
        17 => MatchF18(ast),
        18 => MatchF19(ast),
        19 => MatchF20(ast),
        _ => false
    };

    private static bool MatchModule(Ast? actual) => actual is Block
    {
        HasBraces: false,
        Statements: [var f01, var f02, var f03, var f04, var f05, var f06, var f07, var f08, var f09, var f10,
                     var f11, var f12, var f13, var f14, var f15, var f16, var f17, var f18, var f19, var f20]
    }
        && MatchF01(f01) && MatchF02(f02) && MatchF03(f03) && MatchF04(f04) && MatchF05(f05)
        && MatchF06(f06) && MatchF07(f07) && MatchF08(f08) && MatchF09(f09) && MatchF10(f10)
        && MatchF11(f11) && MatchF12(f12) && MatchF13(f13) && MatchF14(f14) && MatchF15(f15)
        && MatchF16(f16) && MatchF17(f17) && MatchF18(f18) && MatchF19(f19) && MatchF20(f20);

    private static bool MatchF01(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f01"),
        Parameters: [],
        Body: Block
        {
            HasBraces: true,
            Statements: [ReturnStmt { Value: Number { Value: 0 } }]
        }
    };

    private static bool MatchF02(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f02"),
        Parameters: [Identifier("x")],
        Body: Block
        {
            HasBraces: true,
            Statements: [ReturnStmt { Value: Identifier("x") }]
        }
    };

    private static bool MatchF03(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f03"),
        Parameters: [Identifier("x"), Identifier("y")],
        Body: Block
        {
            HasBraces: true,
            Statements: [ReturnStmt { Value: BinaryExpr { Op: "+", Left: Identifier("x"), Right: Identifier("y") } }]
        }
    };

    private static bool MatchF04(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f04"),
        Parameters: [Identifier("x"), Identifier("y"), Identifier("z")],
        Body: Block
        {
            HasBraces: true,
            Statements: [ReturnStmt { Value: BinaryExpr { Op: "-", Left: BinaryExpr { Op: "-", Left: Identifier("x"), Right: Identifier("y") }, Right: Identifier("z") } }]
        }
    };

    private static bool MatchF05(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f05"),
        Parameters: [Identifier("x"), Identifier("y"), Identifier("z")],
        Body: Block
        {
            HasBraces: true,
            Statements: [ReturnStmt { Value: BinaryExpr { Op: "/", Left: BinaryExpr { Op: "*", Left: Identifier("x"), Right: Identifier("y") }, Right: Identifier("z") } }]
        }
    };

    private static bool MatchF06(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f06"),
        Parameters: [Identifier("x"), Identifier("y"), Identifier("z")],
        Body: Block
        {
            HasBraces: true,
            Statements: [ReturnStmt { Value: BinaryExpr { Op: "&&", Left: BinaryExpr { Op: "<", Left: Identifier("x"), Right: Identifier("y") }, Right: BinaryExpr { Op: "<=", Left: Identifier("y"), Right: Identifier("z") } } }]
        }
    };

    private static bool MatchF07(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f07"),
        Parameters: [Identifier("x"), Identifier("y"), Identifier("z")],
        Body: Block
        {
            HasBraces: true,
            Statements: [ReturnStmt { Value: BinaryExpr { Op: "||", Left: BinaryExpr { Op: ">", Left: Identifier("x"), Right: Identifier("y") }, Right: BinaryExpr { Op: ">=", Left: Identifier("y"), Right: Identifier("z") } } }]
        }
    };

    private static bool MatchF08(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f08"),
        Parameters: [Identifier("x"), Identifier("y"), Identifier("z")],
        Body: Block
        {
            HasBraces: true,
            Statements: [ReturnStmt { Value: BinaryExpr { Op: "!=", Left: BinaryExpr { Op: "==", Left: Identifier("x"), Right: Identifier("y") }, Right: Identifier("z") } }]
        }
    };

    private static bool MatchF09(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f09"),
        Parameters: [Identifier("x"), Identifier("y")],
        Body: Block
        {
            HasBraces: true,
            Statements: [ReturnStmt { Value: BinaryExpr { Op: "*", Left: UnaryExpr { Op: "-", Expr: BinaryExpr { Op: "+", Left: Identifier("x"), Right: Identifier("y") } }, Right: Number { Value: 2 } } }]
        }
    };

    private static bool MatchF10(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f10"),
        Parameters: [],
        Body: Block
        {
            HasBraces: true,
            Statements: [ReturnStmt { Value: CallExpr { Name: Identifier("f01"), Arguments: Args { Arguments: [] } } }]
        }
    };

    private static bool MatchF11(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f11"),
        Parameters: [Identifier("x")],
        Body: Block
        {
            HasBraces: true,
            Statements: [ReturnStmt { Value: CallExpr { Name: Identifier("f01"), Arguments: Args { Arguments: [Identifier("x")] } } }]
        }
    };

    private static bool MatchF12(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f12"),
        Parameters: [Identifier("x"), Identifier("y"), Identifier("z")],
        Body: Block
        {
            HasBraces: true,
            Statements: [ReturnStmt { Value: CallExpr { Name: Identifier("f01"), Arguments: Args { Arguments: [Identifier("x"), Identifier("y"), Identifier("z")] } } }]
        }
    };

    private static bool MatchF13(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f13"),
        Parameters: [Identifier("x"), Identifier("y")],
        Body: Block
        {
            HasBraces: true,
            Statements: [ReturnStmt { Value: CallExpr { Name: Identifier("f02"), Arguments: Args { Arguments: [BinaryExpr { Op: "+", Left: Identifier("x"), Right: Identifier("y") }] } } }]
        }
    };

    private static bool MatchF14(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f14"),
        Parameters: [Identifier("x")],
        Body: Block
        {
            HasBraces: true,
            Statements:
            [
                VarDecl { Type: Token("int"), Name: Identifier("a") },
                VarDecl { Type: Token("int"), Name: Identifier("b") },
                ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("a"), Right: BinaryExpr { Op: "=", Left: Identifier("b"), Right: Number { Value: 5 } } } },
                ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("b"), Right: BinaryExpr { Op: "+", Left: Identifier("x"), Right: Identifier("a") } } },
                ReturnStmt { Value: Identifier("a") }
            ]
        }
    };

    private static bool MatchF15(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f15"),
        Parameters: [Identifier("x")],
        Body: Block
        {
            HasBraces: true,
            Statements:
            [
                IfStatement
            {
                Condition: BinaryExpr { Op: ">", Left: Identifier("x"), Right: Number { Value: 0 } },
                Then: Block
                {
                    HasBraces: false,
                    Statements: [ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("x"), Right: Number { Value: 1 } } }]
                },
                Else: null
            },
                ReturnStmt { Value: Identifier("x") }
            ]
        }
    };

    private static bool MatchF16(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f16"),
        Parameters: [Identifier("x")],
        Body: Block
        {
            HasBraces: true,
            Statements:
            [
                VarDecl { Type: Token("int"), Name: Identifier("y") },
                IfStatement
            {
                Condition: BinaryExpr { Op: ">", Left: Identifier("x"), Right: Number { Value: 0 } },
                Then: Block
                {
                    HasBraces: true,
                    Statements: [ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("y"), Right: Number { Value: 1 } } }]
                },
                Else: Block
                {
                    HasBraces: true,
                    Statements: [ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("y"), Right: Number { Value: 0 } } }]
                }
            },
                ReturnStmt { Value: Identifier("y") }
            ]
        }
    };

    private static bool MatchF17(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f17"),
        Parameters: [],
        Body: Block
        {
            HasBraces: true,
            Statements:
            [
                ArrayDecl { Type: Token("int"), Name: Identifier("a"), Parameters: [Number { Value: 1 }, Number { Value: 2 }, Number { Value: 3 }] },
                ArrayDecl { Type: Token("int"), Name: Identifier("b"), Parameters: [] },
                ReturnStmt { Value: Number { Value: 42 } }
            ]
        }
    };

    private static bool MatchF18(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f18"),
        Parameters: [Identifier("x"), Identifier("y"), Identifier("z")],
        Body: Block
        {
            HasBraces: true,
            Statements: [ReturnStmt { Value: BinaryExpr { Op: "/", Left: BinaryExpr { Op: "*", Left: BinaryExpr { Op: "+", Left: Identifier("x"), Right: Identifier("y") }, Right: BinaryExpr { Op: "-", Left: Identifier("z"), Right: Number { Value: 1 } } }, Right: Number { Value: 2 } } }]
        }
    };

    private static bool MatchF19(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f19"),
        Parameters: [Identifier("x"), Identifier("y"), Identifier("z")],
        Body: Block
        {
            HasBraces: true,
            Statements:
            [
                VarDecl { Type: Token("int"), Name: Identifier("a") },
                VarDecl { Type: Token("int"), Name: Identifier("b") },
                ArrayDecl { Type: Token("int"), Name: Identifier("t"), Parameters: [Number { Value: 1 }, Number { Value: 2 }] },
                ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("a"), Right: BinaryExpr { Op: "+", Left: BinaryExpr { Op: "*", Left: Identifier("x"), Right: Number { Value: 2 } }, Right: Identifier("y") } } },
                ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("b"), Right: BinaryExpr { Op: "-", Left: Identifier("z"), Right: Number { Value: 1 } } } },
                IfStatement
            {
                Condition: BinaryExpr { Op: ">", Left: Identifier("a"), Right: Identifier("b") },
                Then: Block
                {
                    HasBraces: true,
                    Statements: [ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("a"), Right: Identifier("b") } }]
                },
                Else: Block
                {
                    HasBraces: true,
                    Statements: [ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("b"), Right: Identifier("a") } }]
                }
            },
                IfStatement
            {
                Condition: BinaryExpr { Op: "==", Left: Identifier("a"), Right: Identifier("b") },
                Then: Block
                {
                    HasBraces: false,
                    Statements: [ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("a"), Right: CallExpr { Name: Identifier("f02"), Arguments: Args { Arguments: [Identifier("a")] } } } }]
                },
                Else: null
            },
                ReturnStmt { Value: BinaryExpr { Op: "+", Left: Identifier("a"), Right: Identifier("b") } }
            ]
        }
    };

    private static bool MatchF20(Ast actual) => actual is FunctionDecl
    {
        Name: Identifier("f20"),
        Parameters: [Identifier("x"), Identifier("y"), Identifier("z")],
        Body: Block
        {
            HasBraces: true,
            Statements:
            [
                VarDecl { Type: Token("int"), Name: Identifier("a") },
                VarDecl { Type: Token("int"), Name: Identifier("b") },
                VarDecl { Type: Token("int"), Name: Identifier("c") },
                ArrayDecl { Type: Token("int"), Name: Identifier("v"), Parameters: [Number { Value: 1 }, Number { Value: 2 }, Number { Value: 3 }, Number { Value: 4 }] },
                ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("a"), Right: BinaryExpr { Op: "+", Left: Identifier("x"), Right: Identifier("y") } } },
                ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("b"), Right: BinaryExpr { Op: "-", Left: Identifier("y"), Right: Identifier("z") } } },
                ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("c"), Right: BinaryExpr { Op: "/", Left: BinaryExpr { Op: "*", Left: Identifier("a"), Right: Identifier("b") }, Right: Number { Value: 2 } } } },
                IfStatement
            {
                Condition: BinaryExpr { Op: "&&", Left: BinaryExpr { Op: ">", Left: Identifier("c"), Right: Number { Value: 0 } }, Right: BinaryExpr { Op: ">=", Left: Identifier("a"), Right: Number { Value: 1 } } },
                Then: Block
                {
                    HasBraces: true,
                    Statements: [ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("c"), Right: BinaryExpr { Op: "-", Left: Identifier("c"), Right: Number { Value: 1 } } } }]
                },
                Else: Block
                {
                    HasBraces: true,
                    Statements:
                        [
                            IfStatement
                        {
                            Condition: BinaryExpr { Op: "||", Left: BinaryExpr { Op: "<", Left: Identifier("c"), Right: Number { Value: 0 } }, Right: BinaryExpr { Op: "<=", Left: Identifier("a"), Right: Number { Value: 0 } } },
                            Then: Block
                            {
                                HasBraces: true,
                                Statements: [ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("c"), Right: BinaryExpr { Op: "+", Left: Identifier("c"), Right: Number { Value: 1 } } } }]
                            },
                            Else: null
                        }
                        ]
                }
            },
                ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("a"), Right: CallExpr { Name: Identifier("f03"), Arguments: Args { Arguments: [Identifier("x"), Identifier("y"), Identifier("z")] } } } },
                ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("b"), Right: CallExpr { Name: Identifier("f12"), Arguments: Args { Arguments: [Identifier("a"), Identifier("b"), Identifier("c")] } } } },
                IfStatement
            {
                Condition: BinaryExpr { Op: "==", Left: Identifier("a"), Right: Identifier("b") },
                Then: Block
                {
                    HasBraces: false,
                    Statements: [ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("a"), Right: Number { Value: 1 } } }]
                },
                Else: Block
                {
                    HasBraces: false,
                    Statements: [ExprStmt { Expr: BinaryExpr { Op: "=", Left: Identifier("a"), Right: Number { Value: 0 } } }]
                }
            },
                ReturnStmt { Value: Identifier("c") }
            ]
        }
    };
}
