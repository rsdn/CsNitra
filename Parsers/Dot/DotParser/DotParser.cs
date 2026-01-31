global using Error = (string Input, int Pos, (int Line, int Col) Location, ExtensibleParaser.Terminal[] Expecteds);

using ExtensibleParaser;

namespace Dot;

public class DotParser
{
    private readonly Parser _parser;

    public DotParser()
    {
        _parser = new Parser(DotTerminals.Trivia());

        _parser.Rules["Graph"] =
        [
            new Seq([
                new Literal("digraph"),
                DotTerminals.Identifier(),
                new Literal("{"),
                new ZeroOrMany(new Ref("Statement"), Kind: "Statements"),
                new Literal("}")
            ], "Graph")
        ];

        _parser.Rules["Statement"] =
        [
            new Ref("NodeStatement"),
            new Ref("EdgeStatement"),
            new Ref("Subgraph"),
            new Ref("Assignment"),
            new Ref("AttributeList")
        ];

        _parser.Rules["NodeStatement"] =
        [
            new Seq([
                DotTerminals.Identifier(),
                new Optional(new Ref("AttributeList")),
                new Literal(";")
            ], "NodeStatement")
        ];

        _parser.Rules["EdgeStatement"] =
        [
            new Seq([
                DotTerminals.Identifier(),
                new Literal("->"),
                DotTerminals.Identifier(),
                new Optional(new Ref("AttributeList")),
                new Literal(";")
            ], "EdgeStatement")
        ];

        _parser.Rules["Subgraph"] =
        [
            new Seq([
                new Literal("subgraph"),
                DotTerminals.Identifier(),
                new Literal("{"),
                new ZeroOrMany(new Ref("Statement"), Kind: "Statements"),
                new Literal("}")
            ], "Subgraph")
        ];

        _parser.Rules["Assignment"] =
        [
            new Seq([
                DotTerminals.Identifier(),
                new Literal("="),
                new Ref("Value"),
                new Literal(";")
            ], "Assignment")
        ];

        _parser.Rules["AttributeList"] =
        [
            new Seq([
                new Literal("["),
                new Ref("Attribute"),
                new ZeroOrMany(new Seq([
                    new Literal(","),
                    new Ref("Attribute")
                ], "AttributeRest"), "AttributeRestList"),
                new Literal("]")
            ], "AttributeList")
        ];

        _parser.Rules["Attribute"] =
        [
            new Seq([
                DotTerminals.Identifier(),
                new Literal("="),
                new Ref("Value")
            ], "Attribute")
        ];

        _parser.Rules["Value"] =
        [
            DotTerminals.Identifier(),
            DotTerminals.QuotedString(),
            DotTerminals.Number()
        ];

        _parser.BuildTdoppRules();
    }

    public Result Parse(string input)
    {
        var result = _parser.Parse(input, "Graph", out _);
        if (result.TryGetSuccess(out var node, out _))
            return new Result.Success(node);
        return new Result.Failure(_parser.ErrorInfo.AssertIsNonNull());
    }

    public DotGraph ParseDotGraph(string input)
    {
        var result = _parser.Parse(input, "Graph", out _);
        if (!result.TryGetSuccess(out var node, out _))
            throw new InvalidOperationException($"❌ Parse FAILED. {_parser.ErrorInfo.AssertIsNonNull().GetErrorText()}");

        var visitor = new DotVisitor(input);
        node.Accept(visitor);
        return visitor.Result.AssertIsNonNull();
    }

    public abstract record Result
    {
        public sealed record Success(ISyntaxNode RootNode) : Result;
        public sealed record Failure(FatalError Error) : Result;
    }
}

