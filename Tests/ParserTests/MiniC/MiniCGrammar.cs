#nullable enable

using ExtensibleParser;

namespace MiniC;

public static class MiniCGrammar
{
    public static void ConfigureRules(Parser parser)
    {
        var closingParenthesis = new OftenMissed(new Literal("}"));
        var closingBracket = new OftenMissed(new Literal(")"));

        // Expression rules
        parser.Rules["Expr"] = new Rule[]
        {
            Terminals.Number(),
            Terminals.Ident(),
            new Seq([new Literal("("), new Ref("Expr"), new Literal(")")], "Parens"),
            new Seq([Terminals.Ident(), new Literal("("), closingBracket], "CallNoArgs"),
            new Seq([Terminals.Ident(), new Literal("("), new SeparatedList(new Ref("Expr"), new Literal(","), Kind: "ArgsRest", EndBehavior: SeparatorEndBehavior.Forbidden), closingBracket], "Call"),
            new Seq([new Literal("-"), new ReqRef("Expr", 300)], "Neg"),

            new Seq([new Ref("Expr"), new Literal("*"), new ReqRef("Expr", 200)], "Mul"),
            new Seq([new Ref("Expr"), new Literal("/"), new ReqRef("Expr", 200)], "Div"),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
            new Seq([new Ref("Expr"), new Literal("-"), new ReqRef("Expr", 100)], "Sub"),
            new Seq([new Ref("Expr"), new Literal("=="), new ReqRef("Expr", 50)], "Eq"),
            new Seq([new Ref("Expr"), new Literal("!="), new ReqRef("Expr", 50)], "Neq"),
            new Seq([new Ref("Expr"), new Literal("<"), new ReqRef("Expr", 50)], "Lt"),
            new Seq([new Ref("Expr"), new Literal(">"), new ReqRef("Expr", 50)], "Gt"),
            new Seq([new Ref("Expr"), new Literal("<="), new ReqRef("Expr", 50)], "Le"),
            new Seq([new Ref("Expr"), new Literal(">="), new ReqRef("Expr", 50)], "Ge"),
            new Seq([new Ref("Expr"), new Literal("&&"), new ReqRef("Expr", 30)], "And"),
            new Seq([new Ref("Expr"), new Literal("||"), new ReqRef("Expr", 20)], "Or"),
            new Seq([new Ref("Expr"), new Literal("="), new ReqRef("Expr", 10, Right: true)], "AssignmentExpr"),

            // Recovery rules:
            new Seq([new Ref("Expr"), Terminals.ErrorOperator(), new ReqRef("Expr", 200)], "RecoveryOperator"),
            new Seq([new Ref("Expr"), Terminals.ErrorEmpty(), new ReqRef("Expr", 200)], "RecoveryEmptyOperator"),
            new RecoveryRule(Terminals.ErrorEmpty()),
        };

        // Statement rules. Return — ПЕРЕД ExprStmt: «return» совпадает с Ident, и без этого порядка
        // при равной длине победил бы ExprStmt («return» как идентификатор) — например, для «return -(...)»
        // и «return (...)».
        parser.Rules["Statement"] = new Rule[]
        {
            new Seq([new Literal("return"), new Ref("Expr"), new OftenMissed(new Literal(";"))], "Return"),
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
                    new Ref("Block"), new Literal("else"), new Ref("Block")], "IfElseStmt")
        };

        // Block rules
        parser.Rules["Block"] =
        [
            new Seq([new Literal("{"), new ZeroOrMany(new Seq([new NotPredicate(new Ref("Function")), new Ref("Statement")], "BlockStatement")), closingParenthesis], "MultiBlock"),
            new Ref("Statement", "SimplBlock")
        ];

        parser.Rules["Params"] = [
            new SeparatedList(Terminals.Ident(), new Literal(","), Kind: "ParamsRest", EndBehavior: SeparatorEndBehavior.Forbidden, CanBeEmpty: false),
            new Literal("void", "VoidParams"),
        ];

        // Function declaration
        parser.Rules["Function"] = [
            new Seq([
                new Literal("int"),
                Terminals.Ident(),
                new Literal("("),
                new Optional(new Ref("Params")),
                closingBracket,
                new Ref("Block")
            ], "FunctionDecl")
        ];

        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Function"), "ModuleFunctions")];

        parser.BuildTdoppRules();
    }
}
