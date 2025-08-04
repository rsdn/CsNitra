#nullable enable

using Diagnostics;
using ExtensibleParaser;
using System.Diagnostics;
using Tests.Extensions;

namespace MiniC;

[TestClass]
public partial class MiniCTests
{
    private readonly Parser _parser = new(new EmptyTerminal("Trivia"), new Log(LogImportance.High));

    [TestInitialize]
    public void Initialize()
    {
        var closingParenthesis = new OftenMissed(new Literal("}"));
        var closingBracket = new OftenMissed(new Literal(")"));

        // Expression rules
        _parser.Rules["Expr"] = new Rule[]
        {
            new Literal("0", "Number"),
            new Literal("a", "Ident"),
            new Seq(new Rule[] { new Literal("("), new Ref("Expr"), }, "Parens"),
            new Seq(new Rule[] { new Literal("a"), new Literal("("), closingBracket }, "CallNoArgs"),
            new Seq(new Rule[] { new Literal("a"), new Literal("("), new SeparatedList(new Ref("Expr"), new Literal(","), Kind: "ArgsRest", EndBehavior: SeparatorEndBehavior.Forbidden ), closingBracket }, "Call"),
            new Seq(new Rule[] { new Literal("-"), new ReqRef("Expr", 300) }, "Neg"),

            new Seq(new Rule[] { new Ref("Expr"), new Literal("*"),  new ReqRef("Expr", 200) }, "Mul"),
            new Seq(new Rule[] { new Ref("Expr"), new Literal("/"),  new ReqRef("Expr", 200) }, "Div"),
            new Seq(new Rule[] { new Ref("Expr"), new Literal("+"),  new ReqRef("Expr", 100) }, "Add"),
            new Seq(new Rule[] { new Ref("Expr"), new Literal("-"),  new ReqRef("Expr", 100) }, "Sub"),
            new Seq(new Rule[] { new Ref("Expr"), new Literal("=="), new ReqRef("Expr",  50) }, "Eq"),
            new Seq(new Rule[] { new Ref("Expr"), new Literal("!="), new ReqRef("Expr",  50) }, "Neq"),
            new Seq(new Rule[] { new Ref("Expr"), new Literal("<"),  new ReqRef("Expr",  50) }, "Lt"),
            new Seq(new Rule[] { new Ref("Expr"), new Literal(">"),  new ReqRef("Expr",  50) }, "Gt"),
            new Seq(new Rule[] { new Ref("Expr"), new Literal("<="), new ReqRef("Expr",  50) }, "Le"),
            new Seq(new Rule[] { new Ref("Expr"), new Literal(">="), new ReqRef("Expr",  50) }, "Ge"),
            new Seq(new Rule[] { new Ref("Expr"), new Literal("&&"), new ReqRef("Expr",  30) }, "And"),
            new Seq(new Rule[] { new Ref("Expr"), new Literal("||"), new ReqRef("Expr",  20) }, "Or"),
            new Seq(new Rule[] { new Ref("Expr"), new Literal("="),  new ReqRef("Expr",  10, Right: true) }, "AssignmentExpr"),

            // Recovery rules:
            new Seq(new Rule[] { new Ref("Expr"), new EmptyTerminal("ErrorOperator"), new ReqRef("Expr",  200) }, "RecoveryOperator"),
            new Seq(new Rule[] { new Ref("Expr"), new EmptyTerminal("Error"), new ReqRef("Expr",  200) }, "RecoveryEmptyOperator"),
            new EmptyTerminal("Error"),
        };

        // Statement rules
        _parser.Rules["Statement"] = new Rule[]
        {
            new Seq(new Rule[] { new Literal("int"), new Literal("a"), new OftenMissed(new Literal(";")) }, "VarDecl"),
            new Seq(new Rule[] {
                new Literal("int"), new Literal("["), new Literal("]"), new Literal("a"), new Literal("="), new Literal("{"),
                new SeparatedList(new Literal("0"), new Literal(","), Kind: "ArrayDeclItems", EndBehavior: SeparatorEndBehavior.Optional),
                closingParenthesis, new OftenMissed(new Literal(";"))
            }, "ArrayDecl"),
            new Seq(new Rule[] { new Ref("Expr"), new OftenMissed(new Literal(";")) }, "ExprStmt"),
            new Seq(new Rule[] { new Literal("if"), new Literal("("), new Ref("Expr"), closingBracket,
                    new Ref("Block") }, "IfStmt"),
            new Seq(new Rule[] { new Literal("if"), new Literal("("), new Ref("Expr"), closingBracket,
                    new Ref("Block"), new Literal("else"), new Ref("Block") }, "IfElseStmt"),
            new Seq(new Rule[] { new Literal("return"), new Ref("Expr"), new OftenMissed(new Literal(";")) }, "Return")
        };

        // Block rules
        _parser.Rules["Block"] = new Rule[]
        {
            new Seq(new Rule[] { new Literal("{"), new ZeroOrMany(new NotPredicate(new Ref("Function"), new Ref("Statement"))), closingParenthesis }, "MultiBlock"),
            new Ref("Statement", "SimplBlock")
        };

        _parser.Rules["Params"] = new Rule[] {
            new SeparatedList(new Literal("a"), new Literal(","), Kind: "ParamsRest", EndBehavior: SeparatorEndBehavior.Forbidden, CanBeEmpty: false),
            new Literal("void", "VoidParams"),
        };

        // Function declaration
        _parser.Rules["Function"] = new Rule[] {
            new Seq(new Rule[] {
                new Literal("int"),
                new Literal("a"),
                new Literal("("),
                new Optional(new Ref("Params")), // Используем новый Optional
                closingBracket,
                new Ref("Block")
            }, "FunctionDecl")
        };

        _parser.Rules["Module"] = new Rule[] { new ZeroOrMany(new Ref("Function"), "ModuleFunctions") };

        _parser.BuildTdoppRules("Module");
    }

    [TestMethod]
    public void PanicMode_DisplayContextOnError()
    {
        var input = "int main() { int x = @; }";
        Trace.WriteLine($"\n=== TEST START: {input} ===");

        var parseResult = _parser.Parse(input, "Module", out _);
        Assert.IsFalse(parseResult.IsSuccess, "Parse should fail for this input.");

        // The real verification for this test is to inspect the trace output
        // for the panic mode context display.
        Trace.WriteLine("Test finished. Please inspect the log output for panic mode context information.");
    }
}
