using ExtensibleParaser;

namespace Json;

public class JsonParser
{
    private readonly Parser _parser;

    public JsonParser()
    {
        _parser = new Parser(JsonTerminals.Whitespace());

        _parser.Rules["Json"] = [new Ref("Value")];

        _parser.Rules["Value"] = [
            new Seq([
                new Literal("{"),
                new SeparatedList(
                    new Seq([
                        JsonTerminals.String(),
                        new Literal(":"),
                        new Ref("Value")
                    ], "Property"),
                    new Literal(","),
                    "Properties",
                    SeparatorEndBehavior.Forbidden,
                    CanBeEmpty: true
                ),
                new Literal("}")
            ], "Object"),
            new Seq([
                new Literal("["),
                new SeparatedList(
                    new Ref("Value"),
                    new Literal(","),
                    "Elements",
                    SeparatorEndBehavior.Forbidden,
                    CanBeEmpty: true
                ),
                new Literal("]")
            ], "Array"),
            JsonTerminals.String(),
            JsonTerminals.Number(),
            JsonTerminals.True(),
            JsonTerminals.False(),
            JsonTerminals.Null()
        ];

        _parser.BuildTdoppRules("Json");
    }

    public JsonAst Parse(string input)
    {
        var result = _parser.Parse(input, "Json", out _);
        if (!result.TryGetSuccess(out var node, out _))
            throw new InvalidOperationException($"Parse failed: {_parser.ErrorInfo.GetErrorText()}");

        var visitor = new JsonVisitor(input);
        node.Accept(visitor);
        return visitor.Result ?? throw new InvalidOperationException("Parsing resulted in null AST");
    }
}
