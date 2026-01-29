using CsNitra.Ast;
using ExtensibleParaser;

namespace CsNitra;

public static class ParserExtensions
{
    public static void BuildFromAst(this Parser parser, GrammarAst grammar, Source source, IEnumerable<Terminal> terminals)
    {
        var typeChecker = new TypeChecker(source, terminals.Select(t => (t.Kind, t)));
        var (diagnostics, globalScope) = typeChecker.CheckGrammar(grammar);

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            var errors = string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"Type checking failed:\n{errors}");
        }

        var generator = new RuleGenerator(globalScope, parser);
        generator.GenerateRules();
    }
}
