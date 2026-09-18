using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CsNitra;
using CsNitra.Ast;
using CsNitra.TypeChecking;
using ExtensibleParser;

namespace CsPreprocessor;

public sealed class Preprocessor
{
    private const string StartRule = "PreprocessorFile";

    private const string GrammarResourceSuffix = "Preprocessor.grammar";

    public static PreprocessResult Run(string source, IEnumerable<string> commandLineSymbols)
    {
        var parser = BuildParser();
        if (parser.Parse(source, StartRule, out _).TryGetSuccess(out var topNode, out _) && topNode is SeqNode)
        {
            var interpreter = new PreprocessorInterpreter(source, commandLineSymbols);
            topNode.Accept(interpreter);
            return interpreter.Result!;
        }

        return new(source, Array.Empty<Diagnostic>(), Array.Empty<LineDirective>());
    }

    public static Parser BuildParser()
    {
        var grammarText = LoadGrammar();
        var parseResult = new CsNitraParser().Parse<GrammarAst>(grammarText);
        if (parseResult is Failed(var error))
            throw new InvalidOperationException($"Failed to parse grammar '{GrammarResourceSuffix}':\n{error.GetErrorText()}");

        if (parseResult is not Success<GrammarAst>(var grammar))
            throw new InvalidOperationException($"Unexpected grammar parse result: {parseResult.GetType().Name}");

        var parser = new Parser(PreprocessorTerminals.NoOpTrivia());
        parser.BuildFromAst(grammar, new SourceText(grammarText, GrammarResourceSuffix), PreprocessorTerminals.GetAll());
        parser.BuildTdoppRules();
        return parser;
    }

    private static string LoadGrammar()
    {
        var assembly = typeof(Preprocessor).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(n => n.EndsWith(GrammarResourceSuffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{GrammarResourceSuffix}' not found in {assembly.FullName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
