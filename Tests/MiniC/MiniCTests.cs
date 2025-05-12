﻿#nullable enable

using Diagnostics;
using ExtensibleParaser;
using System.Diagnostics;
using Tests.Extensions;

namespace MiniC;

[TestClass]
public partial class MiniCTests
{
    private readonly Parser _parser = new(Terminals.Trivia(), new Log(LogImportance.High));

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
            new Seq([new Literal("("), new Ref("Expr"), ], "Parens"),
            new Seq([Terminals.Ident(), new Literal("("), closingBracket], "CallNoArgs"),
            new Seq([Terminals.Ident(), new Literal("("), new Ref("Expr"), new ZeroOrMany(new Seq([new Literal(","), new Ref("Expr")], Kind: "ArgsRest")), closingBracket], "Call"),
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
            new Seq([new Ref("Expr"), new Literal("&&"), new ReqRef("Expr",  30)], "And"),
            new Seq([new Ref("Expr"), new Literal("||"), new ReqRef("Expr",  20)], "Or"),
            new Seq([new Ref("Expr"), new Literal("="),  new ReqRef("Expr",  10, Right: true)], "AssignmentExpr"),

            // Recovery rules:
            new Seq([new Ref("Expr"), (Terminals.ErrorOperator()), new ReqRef("Expr",  200)], "RecoveryOperator"),
            new Seq([new Ref("Expr"), Terminals.ErrorEmpty(), new ReqRef("Expr",  200)], "RecoveryEmptyOperator"),
            Terminals.ErrorEmpty(),
        };

        // Statement rules
        _parser.Rules["Statement"] = new Rule[]
        {
            new Seq([new Literal("int"), Terminals.Ident(), new OftenMissed(new Literal(";"))], "VarDecl"),
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
            new Seq([new Literal("{"), new ZeroOrMany(new NotPredicate(new Ref("Function"), new Ref("Statement"))), closingParenthesis], "MultiBlock"),
            new Ref("Statement", "SimplBlock")
        ];

        _parser.Rules["Params"] = [
            new Seq([Terminals.Ident(), new ZeroOrMany(new Seq([new Literal(","), Terminals.Ident()], Kind: "ParamsRest"))], "ParamsList"),
            new Literal("void", "VoidParams"),
        ];

        // Function declaration
        _parser.Rules["Function"] = [
            new Seq([
                new Literal("int"),
                Terminals.Ident(),
                new Literal("("),
                new Optional(new Ref("Params")), // Используем новый Optional
                closingBracket,
                new Ref("Block")
            ], "FunctionDecl")
        ];

        _parser.Rules["Module"] = [new ZeroOrMany(new Ref("Function"), "ModuleFunctions")];

        _parser.BuildTdoppRules();
    }

    [TestMethod]
    public void ModuleWithMultipleFunctions()
    {
        TestMiniC(
            "Module",
            """
        int func1() { return 1; }
        
        int func2(x)
        {
            return x;
        }
        
        int main()
        {
            return 0;
        }
        """,
            "FunctionDecl: func1() { Return(1) }; FunctionDecl: func2(x) { Return(x) }; FunctionDecl: main() { Return(0) }"
        );
    }

    [TestMethod]
    public void Err_MissingOperatorExpression()
    {
        TestMiniC(
            "Function",
            """
            int func(x, y)
            {
                int z;
                z = y  x;
                return z * y;
            }
            """,
            "FunctionDecl: func(x, y) { VarDecl: z; ExprStmt: (z = (y «Missing operator» x)); Return((z * y)) }"
        );
    }

    [TestMethod]
    public void Err_MissingFirstExpression()
    {
        TestMiniC(
            "Function",
            """
            int func(x, y)
            {
                int z;
                z =  + x;
                return z * y;
            }
            """,
            "FunctionDecl: func(x, y) { VarDecl: z; ExprStmt: (z = («Error: expected Expr» + x)); Return((z * y)) }"
        );
    }

    [TestMethod]
    public void Err_MissingSecondExpression()
    {
        TestMiniC(
            "Function",
            """
            int func(x, y)
            {
                int z;
                z = x + ;
                return z * y;
            }
            """,
            "FunctionDecl: func(x, y) { VarDecl: z; ExprStmt: (z = (x + «Error: expected Expr»)); Return((z * y)) }"
        );
    }

    [TestMethod]
    public void Err_UnexpectedTerminalExpression()
    {
        TestMiniC(
            "Function",
            """
            int func(x, y)
            {
                int z;
                z = x % 5;
                return z + y;
            }
            """,
            "FunctionDecl: func(x, y) { VarDecl: z; ExprStmt: (z = (x «Unexpected: %» 5)); Return((z + y)) }"
        );
    }

    [TestMethod]
    public void Err_MultipleUnexpectedTerminalExpression()
    {
        TestMiniC(
            "Module",
            """
            int func1(x, y)
            {
                int z;
                z = x % 5;
                return z;
            }

            int func2(x, y)
            {
                int z;
                z = x $ 5;
                return z;
            }
            """,
            "FunctionDecl: func1(x, y) { VarDecl: z; ExprStmt: (z = (x «Unexpected: %» 5)); Return(z) }; FunctionDecl: func2(x, y) { VarDecl: z; ExprStmt: (z = (x «Unexpected: $» 5)); Return(z) }"
        );
    }

    [TestMethod]
    public void Err_MissingClosingBraceWithFunctionInside()
    {
        TestMiniC(
            "Module",
            """
            int func1(x, y)
            {
                int z;
                z = x / 5;
                return z;
            

            int func2(x, y)
            {
                int z;
                z = x + y;
                return z;
            }
            """,
            "FunctionDecl: func1(x, y) { VarDecl: z; ExprStmt: (z = (x / 5)); Return(z) }; FunctionDecl: func2(x, y) { VarDecl: z; ExprStmt: (z = (x + y)); Return(z) }"
        );
    }

    [TestMethod]
    public void Err_Missing2ClosingBraceWithFunctionInside()
    {
        TestMiniC(
            "Module",
            """
            int func1(x, y)
            {
                int z;
                z = x / 5;
                if (z)
                {
                    return x;
                

                return z;
            

            int func2(x, y)
            {
                int z;
                z = x + y;
                return z;
            }
            """,
            "FunctionDecl: func1(x, y) { VarDecl: z; ExprStmt: (z = (x / 5)); IfStmt: z then { Return(x); Return(z) } }; FunctionDecl: func2(x, y) { VarDecl: z; ExprStmt: (z = (x + y)); Return(z) }"
        );
    }

    [TestMethod]
    public void Err_Multiple()
    {
        TestMiniC(
            "Module",
            """
            int func1(x, y)
            {
                int z;
                z = x $^ 5;
                if (z)
                {
                    return x;
                

                return z;

            
            int func2(x, y)
            {
                int z;
                z = x + y;
                return z;
            }
            """,
            "FunctionDecl: func1(x, y) { VarDecl: z; ExprStmt: (z = (x «Unexpected: $^» 5)); IfStmt: z then { Return(x); Return(z) } }; FunctionDecl: func2(x, y) { VarDecl: z; ExprStmt: (z = (x + y)); Return(z) }"
        );
    }


    [TestMethod]
    public void TwoFunctionsModule()
    {
        TestMiniC(
            "Module",
            """
            int func1(x, y)
            {
                int z;
                z = x / 5;
                return z;
            }

            int func2(x, y)
            {
                int z;
                z = x + y;
                return z;
            }
            """,
            "FunctionDecl: func1(x, y) { VarDecl: z; ExprStmt: (z = (x / 5)); Return(z) }; FunctionDecl: func2(x, y) { VarDecl: z; ExprStmt: (z = (x + y)); Return(z) }"
        );
    }

    [TestMethod]
    public void FunctionWithParameters()
    {
        TestMiniC(
            "Function",
            "int func(x, y) { return x + y; }",
            "FunctionDecl: func(x, y) { Return((x + y)) }"
        );
    }

    [TestMethod]
    public void FunctionWithMultipleParameters()
    {
        TestMiniC(
            "Function",
            "int func(a, b, c, d) { return a + b + c + d; }",
            "FunctionDecl: func(a, b, c, d) { Return((((a + b) + c) + d)) }"
        );
    }

    [TestMethod]
    public void FunctionDecl() => TestMiniC(
        "Function",
        "int main() { return 0; }",
        "FunctionDecl: main() { Return(0) }"
    );

    [TestMethod]
    public void Assignment() => TestMiniC(
        "Statement",
        "x = y = 5;",
        "ExprStmt: (x = (y = 5))"
    );

    [TestMethod]
    public void AssignmentExpr() => TestMiniC(
        "Expr",
        "x = y = 5",
        "(x = (y = 5))"
    );

    [TestMethod]
    public void IfWithoutElse() => TestMiniC(
        "Statement",
        "if(x > 0) y = 1;",
        "IfStmt: (x > 0) then ExprStmt: (y = 1)"
    );

    [TestMethod]
    public void IfWithElse() => TestMiniC(
        "Statement",
        "if(x > 0) { y = 1; } else { y = 0; }",
        "IfStmt: (x > 0) then { ExprStmt: (y = 1) } else { ExprStmt: (y = 0) }"
    );

    [TestMethod]
    public void FunctionCallNoArgs() => TestMiniC(
        "Expr",
        "func()",
        "Call: func()"
    );

    [TestMethod]
    public void FunctionCallWithArgs() => TestMiniC(
        "Expr",
        "func(1, 2, 3)",
        "Call: func(1, 2, 3)"
    );

    [TestMethod]
    public void SingleStmtBlock() => TestMiniC(
        "Statement",
        "if(x) y = 1; else y = 0;",
        "IfStmt: x then ExprStmt: (y = 1) else ExprStmt: (y = 0)"
    );

    [TestMethod]
    public void MultiStmtBlock() => TestMiniC(
        "Block",
        "{ int x; x = 5; }",
        "{ VarDecl: x; ExprStmt: (x = 5) }"
    );

    [TestMethod]
    public void LogicalExpr() => TestMiniC(
        "Expr",
        "x > 0 && y < 10 || z == 5",
        "(((x > 0) && (y < 10)) || (z == 5))"
    );

    [TestMethod]
    public void FunctionCallAsStatement() => TestMiniC(
        "Statement",
        "func();",
        "ExprStmt: Call: func()"
    );

    [TestMethod]
    public void ComplexExpressionStatement() => TestMiniC(
        "Statement",
        "x = y + func(z) * 2;",
        "ExprStmt: (x = (y + (Call: func(z) * 2)))"
    );

    [TestMethod]
    public void WhitespaceHandling()
    {
        var result = _parser.Parse("   123   ", "Expr", out var triviaLen);
        Assert.IsTrue(result.TryGetSuccess(out var node, out _));
        Assert.AreEqual(3, triviaLen);
        Assert.AreEqual("123", node.ToString("   123   "));
    }

    private void TestMiniC(string startRule, string input, string expectedAst)
    {
        Trace.WriteLine($"\n=== TEST START: {input} ===");

        var parseResult = _parser.Parse(input, startRule, out _);
        if (!parseResult.TryGetSuccess(out var node, out var pos))
        {
            var errorPos = _parser.ErrorPos;
            var contextStart = Math.Max(0, errorPos - 10);
            var contextLength = Math.Min(20, input.Length - contextStart);
            var context = input.Substring(contextStart, contextLength);

            Trace.WriteLine($"❌ Parse FAILED. {parseResult.GetError()}");
            Trace.WriteLine($"Error context: ...{context}...");
            Trace.WriteLine($"Error position:   {new string(' ', errorPos - contextStart)}^");

            Assert.Fail($"Parse failed for: {input}");
            return;
        }

        Trace.WriteLine($"✅ Parse SUCCESS. Node type: {node.Kind}");
        Trace.WriteLine($"Node structure:\n{node}");

        var visitor = new MiniCVisitor(input);
        node.Accept(visitor);

        if (visitor.Result == null)
        {
            Trace.WriteLine("❌ AST generation FAILED");
            Assert.Fail("AST is null");
            return;
        }

        Trace.WriteLine($"Generated AST: {visitor.Result}");
        Assert.AreEqual(expectedAst.NormalizeEol(), visitor.Result.ToString().NormalizeEol());
    }
}
