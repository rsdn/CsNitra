using ExtensibleParser;

namespace CsNitra;

public partial class CsNitraParser
{
    private readonly Parser _parser;

    /// <summary>
    /// Парсер, используемый для парсинга CsNitra грамматики (доступ для тестирования).
    /// </summary>
    internal Parser InternalParser => _parser;

    public CsNitraParser()
    {
        _parser = new Parser(CsNitraTerminals.Trivia());

        // Grammar = Using* Statement*;
        _parser.Rules["Grammar"] = [
            new Seq([
                new ZeroOrMany(new Ref("Using"), "Usings"),
                new ZeroOrMany(new Ref("Statement"), "Statements")
            ], "Grammar")
        ];

        // QualifiedIdentifier = (Identifier; ".")+;
        _parser.Rules["QualifiedIdentifier"] = [
            new SeparatedList(CsNitraTerminals.Identifier(), new Literal("."), Kind: "QualifiedIdentifier", SeparatorEndBehavior.Forbidden)
        ];

        // Using =
        _parser.Rules["Using"] = [
            // | OpenUsing = "using" QualifiedIdentifier ";"
            new Seq([new Literal("using"), new Ref("QualifiedIdentifier"), new Literal(";")], "OpenUsing"),
            // | AliasUsing = "using" Identifier "=" QualifiedIdentifier ";"
            new Seq([new Literal("using"), CsNitraTerminals.Identifier(), new Literal("="), new Ref("QualifiedIdentifier"), new Literal(";")], "AliasUsing")
        ];

        // Statement =
        _parser.Rules["Statement"] = [
            // | Precedence = "precedence" (Identifier; ",")+ ";"
            new Seq([
                new Literal("precedence"),
                new SeparatedList(CsNitraTerminals.Identifier(), new Literal(","), "Precedences", SeparatorEndBehavior.Forbidden),
                new Literal(";")
            ], "Precedence"),
            // | Rule = Identifier "=" Alternative+ ";"
            new Seq([
                CsNitraTerminals.Identifier(),
                new Literal("="),
                new OneOrMany(new Ref("Alternative"), "Alternatives"),
                new Literal(";")
            ], "Rule"),
            // | SimpleRule = Identifier "=" RuleExpression ";";
            new Seq([
                CsNitraTerminals.Identifier(),
                new Literal("="),
                new Ref("RuleExpression"),
                new Literal(";")
            ], "SimpleRule")
        ];

        // Alternative =
        _parser.Rules["Alternative"] = [
            // | NamedAlternative = "|" Identifier "=" RuleExpression
            new Seq([new Literal("|"), CsNitraTerminals.Identifier(), new Literal("="), new Ref("RuleExpression")], "NamedAlternative"),
            // | AnonymousAlternative = "|" QualifiedIdentifier;
            new Seq([new Literal("|"), new Ref("QualifiedIdentifier")], "AnonymousAlternative"),
        ];

        const int Sequence = 1;
        const int Optional = 2;
        const int Naming = 3;
        const int Predicate = 4;
        const int Postfix = 5;
        //const int Primary = 6;

        _parser.Rules["RuleExpression"] = [
            // prefix rules (Primary)
            CsNitraTerminals.Literal(),
            new Seq([
                new Ref("QualifiedIdentifier", Kind: "Ref"),
                new Optional(
                    new Seq([
                        new Literal(":"),
                        CsNitraTerminals.Identifier(),
                        new Optional(new Seq([new Literal(","), new Ref("Associativity")], "Associativity"))
                    ], "PrecedenceWithAssociativity")
                ),
            ], "RuleRef"),
            new Seq([new Literal("("), new Ref("RuleExpression"), new Literal(")")], "Group"),
            new Seq([
                new Literal("("),
                new Ref("RuleExpression", "Element"),
                new Literal(";"),
                new Ref("RuleExpression", "Separator"),
                new Optional(new Seq([new Literal(":"), new Ref("Modifier")], "SeparatorModifier")),
                new Literal(")"),
                new Ref("Count")
            ], "SeparatedList"),

            // postfix rules (operators)
            new Seq([new ReqRef("RuleExpression", Precedence: Postfix), new Literal("??")], "OftenMissed"),
            new Seq([new ReqRef("RuleExpression", Precedence: Postfix), new Literal("+")], "OneOrMany"),
            new Seq([new ReqRef("RuleExpression", Precedence: Postfix), new Literal("*")], "ZeroOrMany"),
            new Seq([new Literal("&"), new ReqRef("RuleExpression", Precedence: Predicate)], "AndPredicate"),
            new Seq([new Literal("!"), new ReqRef("RuleExpression", Precedence: Predicate)], "NotPredicate"),
            new Seq([CsNitraTerminals.Identifier(), new Literal("="), new ReqRef("RuleExpression", Precedence: Naming)], "Named"),
            new Seq([new ReqRef("RuleExpression", Precedence: Optional), new Literal("?")], "Optional"),
            new Seq([new Ref("RuleExpression"), new ReqRef("RuleExpression", Precedence: Sequence)], "Sequence"),
        ];

        // Associativity = "left" | "right";
        _parser.Rules["Associativity"] = [new Literal("left"), new Literal("right")];
        // Modifier = "?" | "!";
        _parser.Rules["Modifier"] = [new Literal("?"), new Literal("!")];
        // Count = "+" | "*";
        _parser.Rules["Count"] = [new Literal("+"), new Literal("*")];

        _parser.BuildTdoppRules();
    }

    public ParseResult Parse<T>(string input) where T : Ast.CsNitraAst
    {
        var result = _parser.Parse(input, startRule: typeof(T).Name.Replace(oldValue: "Ast", newValue: ""), out _);

        if (_parser.ErrorInfo is { } errorInfo)
            return new Failed(errorInfo);

        if (!result.TryGetSuccess(out var node, out _))
            throw new InvalidOperationException("Failed to parse");

        var visitor = new CsNitraVisitor(input);
        node.Accept(visitor);
        return new Success<T>((T)visitor.Result);
    }
}
