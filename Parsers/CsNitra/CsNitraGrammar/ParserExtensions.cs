using CsNitra.Ast;
using CsNitra.TypeChecking;
using ExtensibleParser;

namespace CsNitra;

public static class ParserExtensions
{
    public static (GrammarAst Grammar, MultiFileSource Source) ParseTexts(IReadOnlyList<(string Text, string Path)> grammarTexts)
    {
        var source = new MultiFileSource(grammarTexts);
        var parseResult = new CsNitraParser().Parse<GrammarAst>(source.Text);

        if (parseResult is Failed(var error))
            throw new InvalidOperationException(
                $"Failed to parse grammar '{string.Join("', '", grammarTexts.Select(t => t.Path))}':\n{error.GetErrorText()}");

        if (parseResult is not Success<GrammarAst>(var grammar))
            throw new InvalidOperationException($"Unexpected grammar parse result: {parseResult.GetType().Name}");

        return (grammar, source);
    }

    public static void BuildFromTexts(this Parser parser, IReadOnlyList<(string Text, string Path)> grammarTexts, IEnumerable<Terminal> terminals)
    {
        var (grammar, source) = ParseTexts(grammarTexts);
        parser.BuildFromAst(grammar, source, terminals);
    }

    public static void BuildFromAst(this Parser parser, GrammarAst grammar, Source source, IEnumerable<Terminal> terminals)
    {
        var simplifier = new AstSimplifier();
        var transformedGrammar = simplifier.Transform(grammar);
        var typeChecker = new TypeChecker(source, terminals);
        var (diagnostics, globalScope) = typeChecker.CheckGrammar(transformedGrammar);

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            var errors = string.Join(
                "\n",
                diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => $"{source.FormatPosition(d.Location.Start)}: {d.Message}"));
            throw new InvalidOperationException($"Type checking failed:\n{errors}");
        }

        var generator = new RuleGenerator(globalScope, parser);
        generator.GenerateRules();
    }
}
