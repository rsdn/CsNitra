using ExtensibleParaser;

namespace Dot;

public class DotParser
{
    private readonly Parser _parser;

    public DotParser()
    {
        _parser = new Parser(DotTerminals.Trivia());

        _parser.Rules["Graph"] = new Rule[]
        {
            new Seq([
                new Literal("digraph"),
                DotTerminals.Identifier(),
                new Literal("{"),
                new ZeroOrMany(new Ref("Statement"), Kind: "Statements"),
                new Literal("}")
            ], "Graph")
        };

        _parser.Rules["Statement"] = new Rule[]
        {
            new Ref("NodeStatement"),
            new Ref("EdgeStatement"),
            new Ref("Subgraph"),
            new Ref("Assignment"),
            new Ref("AttributeList")
        };

        _parser.Rules["NodeStatement"] = new Rule[]
        {
            new Seq([
                DotTerminals.Identifier(),
                new Optional(new Ref("AttributeList")),
                new Literal(";")
            ], "NodeStatement")
        };

        _parser.Rules["EdgeStatement"] = new Rule[]
        {
            new Seq([
                DotTerminals.Identifier(),
                new Literal("->"),
                DotTerminals.Identifier(),
                new Optional(new Ref("AttributeList")),
                new Literal(";")
            ], "EdgeStatement")
        };

        _parser.Rules["Subgraph"] = new Rule[]
        {
            new Seq([
                new Literal("subgraph"),
                DotTerminals.Identifier(),
                new Literal("{"),
                new ZeroOrMany(new Ref("Statement"), Kind: "Statements"),
                new Literal("}")
            ], "Subgraph")
        };

        _parser.Rules["Assignment"] =
        [
            new Seq([
                DotTerminals.Identifier(),
                new Literal("="),
                new Ref("Value"),
                new Literal(";")
            ], "Assignment")
        ];

        _parser.Rules["AttributeList"] = new Rule[]
        {
            new Seq([
                new Literal("["),
                new Ref("Attribute"),
                new ZeroOrMany(new Seq([
                    new Literal(","),
                    new Ref("Attribute")
                ], "AttributeRest"), "AttributeRestList"),
                new Literal("]")
            ], "AttributeList")
        };

        _parser.Rules["Attribute"] = new Rule[]
        {
            new Seq([
                DotTerminals.Identifier(),
                new Literal("="),
                new Ref("Value")
            ], "Attribute")
        };

        _parser.Rules["Value"] = new Rule[]
        {
            DotTerminals.Identifier(),
            DotTerminals.QuotedString(),
            DotTerminals.Number()
        };

        _parser.BuildTdoppRules();
    }

    public DotGraph Parse(string input)
    {
        var result = _parser.Parse(input, "Graph", out _);
        if (!result.TryGetSuccess(out var node, out _))
            throw new InvalidOperationException($"❌ Parse FAILED. {_parser.ErrorInfo.GetErrorText()}");

        var visitor = new DotVisitor(input);
        node.Accept(visitor);
        return visitor.Result.AssertIsNonNull();
    }
}
