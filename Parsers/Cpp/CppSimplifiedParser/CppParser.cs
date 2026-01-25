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
            new Ref("AnonymousNamespaceDecl"),
            new Ref("EnumDecl"),
            new Ref("Braces"),
            new Ref("SkipLine")
        ];

        _parser.Rules["Braces"] = [new Seq([new Literal("{"), program, closingBrace], "Braces")];

        _parser.Rules["NamespaceOrEnumStartOrBrace"] =
        [
            new Ref("NamespaceDecl"),
            new Ref("AnonymousNamespaceDecl"),
            new Ref("EnumDecl"),
            new Ref("Braces"),
            new Literal("{"),
            closingBrace,
        ];

        _parser.Rules["SkipLine"] =
        [
            new Seq([
                new NotPredicate(new Ref("NamespaceOrEnumStartOrBrace")),
                CppTerminals.AnyLine()
            ], "SkipLine")
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

        _parser.Rules["AnonymousNamespaceDecl"] = [
            new Seq([
                new Literal("namespace"),
                new Literal("{"),
                program,
                closingBrace
            ], "AnonymousNamespaceDecl")
        ];

        _parser.Rules["EnumDecl"] = [
            new Seq([
                new Literal("enum"),
                new Optional(new Literal("class")),
                CppTerminals.Identifier(),
                new Optional(new Seq([
                    new Literal(":"),
                    CppTerminals.Identifier()
                ], "EnumType"), "OptionalEnumType"),
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
                ], "EnumValue"))
            ], "EnumMember")
        ];

        _parser.BuildTdoppRules("Program");
    }

    public ParseResult Parse(string input, string startRule = "Program")
    {
        var result = _parser.Parse(input, startRule, out _);

        if (_parser.ErrorInfo is { } errorInfo)
            return new Failed(errorInfo);

        if (!result.TryGetSuccess(out var node, out _))
            throw new InvalidOperationException("Failed to parse");

        var visitor = new CppVisitor(input);
        node.Accept(visitor);
        return new Success<CppProgram>(visitor.Result);
    }


    public CppProgram ParseToAst(string input)
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
