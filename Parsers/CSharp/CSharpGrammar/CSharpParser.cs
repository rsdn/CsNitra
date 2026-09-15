using CsNitra;
using CsNitra.Ast;
using CsNitra.TypeChecking;
using ExtensibleParser;

namespace CSharpGrammar;

public sealed class CSharpParser
{
    public Parser Parser { get; }

    public CSharpParser(string grammarText, Terminal trivia, IEnumerable<Terminal> terminals, string? sourcePath = null)
    {
        var path = sourcePath ?? "grammar";
        Parser = Build(grammarText, trivia, terminals, path);
    }

    public Result Parse(string input, string startRule, out int triviaLength) =>
        Parser.Parse(input, startRule, out triviaLength);

    private static Parser Build(string grammarText, Terminal trivia, IEnumerable<Terminal> terminals, string sourcePath)
    {
        var parseResult = new CsNitraParser().Parse<GrammarAst>(grammarText);
        if (parseResult is Failed(var error))
            throw new InvalidOperationException($"Failed to parse grammar '{sourcePath}':\n{error.GetErrorText()}");

        if (parseResult is not Success<GrammarAst>(var grammar))
            throw new InvalidOperationException($"Unexpected grammar parse result: {parseResult.GetType().Name}");

        var parser = new Parser(trivia);
        parser.BuildFromAst(grammar, new SourceText(grammarText, sourcePath), terminals);
        parser.BuildTdoppRules();
        return parser;
    }
}
