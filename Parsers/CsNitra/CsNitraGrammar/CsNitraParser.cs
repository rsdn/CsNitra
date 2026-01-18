using ExtensibleParaser;

namespace CsNitra;

public class CsNitraParser
{
    private const int Sequence = 1;
    private const int Named = 2;
    private const int UnaryPrefix = 4;
    private const int UnarySuffix = 5;
    private readonly Parser _parser;

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
            new SeparatedList(CsNitraTerminals.Identifier(), new ExtensibleParaser.Literal("."), Kind: "QualifiedIdentifier", SeparatorEndBehavior.Forbidden)
        ];

        // Using =
        _parser.Rules["Using"] = [
            // | Open = "using" QualifiedIdentifier ";"
            new Seq([new ExtensibleParaser.Literal("using"), new Ref("QualifiedIdentifier"), new ExtensibleParaser.Literal(";")], "OpenUsing"),
            // | Alias = "using" Identifier "=" QualifiedIdentifier ";"
            new Seq([new ExtensibleParaser.Literal("using"), CsNitraTerminals.Identifier(), new ExtensibleParaser.Literal("="), new Ref("QualifiedIdentifier"), new ExtensibleParaser.Literal(";")], "AliasUsing")
        ];

        // Statement =
        _parser.Rules["Statement"] = [
            // | Precedence = "precedence" (Identifier; ",")+ ";"
            new Seq([
                new ExtensibleParaser.Literal("precedence"),
                new SeparatedList(CsNitraTerminals.Identifier(), new ExtensibleParaser.Literal(","), "Precedences", SeparatorEndBehavior.Forbidden),
                new ExtensibleParaser.Literal(";")
            ], "Precedence"),
            // | Rule = Identifier "=" "|"? (RuleExpression; "|")+ ";";
            new Seq([
                CsNitraTerminals.Identifier(),
                new ExtensibleParaser.Literal("="),
                new Optional(new ExtensibleParaser.Literal("|")),
                new SeparatedList(new Ref("RuleExpression"), new ExtensibleParaser.Literal("|"), "RuleExpressions", SeparatorEndBehavior.Forbidden),
                new ExtensibleParaser.Literal(";")
            ], "Rule")
        ];

        // RuleExpression = // основное рекурсивное правило с приоритетами
        _parser.Rules["RuleExpression"] = [
            // | Sequence = Left=RuleExpression : Sequence Right=RuleExpression : Sequence // самый низкий приоритет
            new Seq([new Ref("RuleExpression"), new ReqRef("RuleExpression", Precedence: Sequence, Right: false)], "SequenceExpression"),
            // | Named = Name=Identifier "=" RuleExpression : Named
            new Seq([CsNitraTerminals.Identifier(), new ExtensibleParaser.Literal("="), new ReqRef("RuleExpression", Precedence: Named, Right: false)], "Named"),
            
            // | Optional = RuleExpression "?"
            new Seq([new ReqRef("RuleExpression", Precedence: UnarySuffix), new ExtensibleParaser.Literal("?")], "Optional"),
            // | OftenMissed = RuleExpression "??"
            new Seq([new ReqRef("RuleExpression", Precedence: UnarySuffix), new ExtensibleParaser.Literal("??")], "OftenMissed"),
            // | OneOrMany = RuleExpression "+"
            new Seq([new ReqRef("RuleExpression", Precedence: UnarySuffix), new ExtensibleParaser.Literal("+")], "OneOrMany"),
            // | ZeroOrMany = RuleExpression "*"
            new Seq([new ReqRef("RuleExpression", Precedence: UnarySuffix), new ExtensibleParaser.Literal("*")], "ZeroOrMany"),
            
            // | AndPredicate = "&" RuleExpression : UnaryPrefix
            new Seq([new ExtensibleParaser.Literal("&"), new ReqRef("RuleExpression", Precedence: UnaryPrefix, Right: false)], "AndPredicateExpression"),
            // | NotPredicate = "!" RuleExpression : UnaryPrefix
            new Seq([new ExtensibleParaser.Literal("!"), new ReqRef("RuleExpression", Precedence: UnaryPrefix, Right: false)], "NotPredicateExpression"),
            
            // | Literal // Primary (самый высокий приоритет) - базовые элементы
            CsNitraTerminals.StringLiteral(),
            CsNitraTerminals.CharLiteral(),
            
            // | RuleRef = Ref=QualifiedIdentifier (":" Precedence=Identifier ("," Associativity)?)?
            new Seq([
                new Ref("QualifiedIdentifier", Kind: "Ref"),
                new Optional(
                    new Seq([
                        new ExtensibleParaser.Literal(":"),
                        CsNitraTerminals.Identifier(),
                        new Optional(new Seq([new ExtensibleParaser.Literal(","), new Ref("Associativity")], "Associativity"))
                    ], "PrecedenceWithAssociativity")),
            ], "RuleRef"),
            
            // | Group = "(" RuleExpression ")"
            new Seq([new ExtensibleParaser.Literal("("), new Ref("RuleExpression"), new ExtensibleParaser.Literal(")")], "Group"),
            
            // | SeparatedList = "(" RuleExpression ";" RuleExpression SeparatorModifier=(":" Modifier)? ")" Count;
            new Seq([
                new ExtensibleParaser.Literal("("),
                new Ref("RuleExpression", "Element"),
                new ExtensibleParaser.Literal(";"),
                new Ref("RuleExpression", "Separator"),
                new Optional(new Seq([new ExtensibleParaser.Literal(":"), new Ref("Modifier")], "SeparatorModifier")),
                new ExtensibleParaser.Literal(")"),
                new Ref("Count")
            ], "SeparatedListExpression")
        ];

        // Associativity = "left" | "right";
        _parser.Rules["Associativity"] = [new ExtensibleParaser.Literal("left"), new ExtensibleParaser.Literal("right")];
        // Modifier = "?" | "!";
        _parser.Rules["Modifier"] = [new ExtensibleParaser.Literal("?"), new ExtensibleParaser.Literal("!")];
        // Count = "+" | "*";
        _parser.Rules["Count"] = [new ExtensibleParaser.Literal("+"), new ExtensibleParaser.Literal("*")];

        _parser.BuildTdoppRules("Grammar");
    }

    public ParseResult Parse<T>(string input) where T : CsNitraAst
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
