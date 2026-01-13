using ExtensibleParaser;

namespace CppEnumExtractor;

public class CppParser
{
    private readonly Parser _parser;

    public CppParser()
    {
        _parser = new Parser(CppTerminals.Trivia());

        var closingBrace = new Literal("}");

        var program = new ZeroOrMany(new Ref("TopLevel"), "ProgramItems");

        _parser.Rules["Program"] = [program];

        _parser.Rules["TopLevel"] =
        [
            new Ref("NamespaceDecl"),
            new Ref("EnumDecl"),
            new Ref("Braces"),
            new Ref("SkipLine")
        ];

        _parser.Rules["Braces"] = [new Seq([new Literal("{"), program, closingBrace], "Braces")];

        _parser.Rules["NamespaceOrEnumStartOrBrace"] =
        [
            new Ref("NamespaceDecl"),
            new Ref("EnumDecl"),
            new Ref("Braces"),
            new Literal("{"),
            closingBrace,
        ];

        _parser.Rules["SkipLine"] =
        [
            new NotPredicate(
                new Ref("NamespaceOrEnumStartOrBrace"),
                CppTerminals.AnyLine()
            )
        ];

        _parser.Rules["NamespaceDecl"] = [
            new Seq([
                new Literal("namespace"),
                CppTerminals.Identifier(),
                new Literal("{"),
                program,
                closingBrace
            ], "NamespaceDecl")
        ];

        _parser.Rules["EnumDecl"] = [
            new Seq([
                new Literal("enum"),
                CppTerminals.Identifier(),
                new Literal("{"),
                new SeparatedList(
                    new Ref("EnumMember"),
                    new Literal(","),
                    "EnumMembers",
                    SeparatorEndBehavior.Optional,
                    CanBeEmpty: true
                ),
                closingBrace,
            ], "EnumDecl")
        ];

        _parser.Rules["EnumMember"] = [
            new Seq([
                CppTerminals.Identifier(),
                new Optional(new Seq([
                    new Literal("="),
                    CppTerminals.EnumExpression()
                ], "EnumValue"), "OptionalValue")
            ], "EnumMember")
        ];

        _parser.BuildTdoppRules("Program");
    }

    public CppProgram Parse(string input)
    {
        var result = _parser.Parse(input, "Program", out _);
        if (_parser.ErrorInfo is { } error)
            throw new InvalidOperationException($"Parse failed: {error.GetErrorText()}");

        if (!result.TryGetSuccess(out var node, out _))
            throw new InvalidOperationException("Parse failed");

        var visitor = new CppVisitor(input);
        node.Accept(visitor);
        return visitor.Result;
    }
}
