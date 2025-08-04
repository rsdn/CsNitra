#nullable enable

namespace Calc;

using Diagnostics;
using ExtensibleParaser;

using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

[TestClass]
public class CalcTests
{
    private class ExprBuilderVisitor(string input) : ISyntaxVisitor
    {
        public Expr? Result { get; private set; }
        public string Input { get; } = input;

        public void Visit(TerminalNode node)
        {
            if (node.Kind == "Number")
                Result = new NumberExpr(int.Parse(node.AsSpan(Input), NumberStyles.Integer));
            else
                Result = new TerminalExpr(node.AsSpan(Input).ToString());
        }

        public void Visit(SeqNode node)
        {
            var children = new List<Expr>();
            foreach (var element in node.Elements)
            {
                element.Accept(this);
                if (Result != null)
                    children.Add(Result);
            }

            Result = children switch
            {
                [Expr a, TerminalExpr { Value: "+" }, Expr b] => new AddExpr(a, b),
                [Expr a, TerminalExpr { Value: "*" }, Expr b] => new MultiplyExpr(a, b),
                [Expr a, TerminalExpr { Value: "^" }, Expr b] => new PowerExpr(a, b),
                [TerminalExpr { Value: "-" }, Expr expr] => new NegateExpr(expr),
                [TerminalExpr { Value: "(" }, Expr expr, TerminalExpr { Value: ")" }] => expr,
                _ => throw new InvalidOperationException($"Unknown sequence: {node.Elements.Count}")
            };
        }

        public void Visit(ListNode node)
        {
            throw new NotImplementedException("It is skipped in this language..");
        }

        public void Visit(SomeNode node) => node.Value.Accept(this);
        public void Visit(NoneNode node) => Result = null;
    }

    private readonly Parser _parser = new(Terminals.Trivia(), new Log(LogImportance.Non));

    [TestInitialize]
    public void Initialize()
    {
        _parser.Rules["Expr"] = new Rule[]
        {
            Terminals.Number(),
            new Seq([new Literal("("), new Ref("Expr"), new Literal(")")], Kind: "Parens"),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", Precedence: 10)], Kind: "Add"),
            new Seq([new Ref("Expr"), new Literal("*"), new ReqRef("Expr", Precedence: 20)], Kind: "Mul"),
            new Seq([new Ref("Expr"), new Literal("^"), new ReqRef("Expr", Precedence: 30, Right: true)], Kind: "Pow"),
            new Seq([new Literal("-"), new ReqRef("Expr", Precedence: 100)], Kind: "Neg"),
        };

        _parser.BuildTdoppRules("Expr");
    }

    [TestMethod] public void TestSimpleAddition() => TestExpression("1+2", "(1 + 2)", 3);
    [TestMethod] public void TestPrecedence1() => TestExpression("3+2*4", "(3 + (2 * 4))", 11);
    [TestMethod] public void TestPrecedence2() => TestExpression("3+2+5+6*4", "(((3 + 2) + 5) + (6 * 4))", 34);
    [TestMethod] public void TestPrecedence3() => TestExpression("3+2+5", "((3 + 2) + 5)", 10);
    [TestMethod] public void TestPrecedence4() => TestExpression("5+4+1^2^3", "((5 + 4) + (1 ^ (2 ^ 3)))", 5 + 4 + (int)Math.Pow(1, Math.Pow(2, 3)));
    [TestMethod] public void TestParentheses() => TestExpression("(3+2)*4", "((3 + 2) * 4)", 20);
    [TestMethod] public void TestUnary() => TestExpression(" - 1 + 2", "(-1 + 2)", 1);
    [TestMethod] public void TestRightAssociativity() => TestExpression("2^3^4", "(2 ^ (3 ^ 4))", BigInteger.Pow(2, (int)BigInteger.Pow(3, 4)));

    private void TestExpression(string input, string expectedAst, BigInteger expectedResult, [CallerArgumentExpression(nameof(expectedResult))] string? expectedText = null)
    {
        var parseResult = ParseAndBuild(input);
        var result = parseResult.Evaluate();
        var textResult = result.ToString("N0");
        Debug.WriteLine($"\nTesting input: '{input}'");
        Debug.WriteLine($"Generated AST: {parseResult}");
        if (textResult != expectedText)
            Debug.WriteLine($"Evaluation result: {textResult}   {nameof(expectedResult)}: {expectedText}");
        else
            Debug.WriteLine($"Evaluation result: {textResult}");

        Assert.AreEqual(expectedAst, parseResult.ToString());
        Assert.AreEqual(expectedResult, result);

        var debugInfos = _parser.MemoizationVisualazer(input);

        Trace.WriteLine("");
        Trace.WriteLine($"debug output of memoization:");
        foreach (var info in debugInfos)
            Trace.WriteLine($"    {info.Info}");
        Trace.WriteLine($"and of debug output of memoization.");
    }

    private Expr ParseAndBuild(string input)
    {
        var parseResult = _parser.Parse(input, "Expr", out _);

        if (parseResult.TryGetSuccess(out var node, out var newPos))
        {
            Assert.AreEqual(expected: input.Length, actual: newPos);
            var visitor = new ExprBuilderVisitor(input);
            node.Accept(visitor);
            Assert.IsNotNull(visitor.Result);
            return visitor.Result;
        }

        Trace.WriteLine($"❌ Parse FAILED. {_parser.ErrorInfo.GetErrorText()}");
        throw new InternalTestFailureException("Parse FAILED.");
    }

    private abstract record Expr
    {
        public abstract BigInteger Evaluate();
        public abstract override string ToString();
    }

    private record NumberExpr(BigInteger Value) : Expr
    {
        public override BigInteger Evaluate() => Value;
        public override string ToString() => Value.ToString();
    }

    private record TerminalExpr(string Value) : Expr
    {
        public override BigInteger Evaluate() => throw new NotImplementedException();
        public override string ToString() => Value;
    }

    private record AddExpr(Expr Left, Expr Right) : Expr
    {
        public override BigInteger Evaluate() => Left.Evaluate() + Right.Evaluate();
        public override string ToString() => $"({Left} + {Right})";
    }

    private record MultiplyExpr(Expr Left, Expr Right) : Expr
    {
        public override BigInteger Evaluate() => Left.Evaluate() * Right.Evaluate();
        public override string ToString() => $"({Left} * {Right})";
    }

    private record PowerExpr(Expr Left, Expr Right) : Expr
    {
        public override BigInteger Evaluate() => BigInteger.Pow(Left.Evaluate(), (int)Right.Evaluate());
        public override string ToString() => $"({Left} ^ {Right})";
    }

    private record NegateExpr(Expr Inner) : Expr
    {
        public override BigInteger Evaluate() => -Inner.Evaluate();
        public override string ToString() => $"-{Inner}";
    }
}
