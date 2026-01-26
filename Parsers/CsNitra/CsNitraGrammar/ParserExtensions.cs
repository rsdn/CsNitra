namespace CsNitra;

public static class ParserExtensions
{
    public static void BuildFromAst(this ExtensibleParaser.Parser parser, GrammarAst grammar,
        Source source, IEnumerable<(string Name, ExtensibleParaser.Terminal Terminal)> terminals)
    {
        // 1. Типизация
        var typeChecker = new TypeChecker(source, terminals);
        var (diagnostics, globalScope) = typeChecker.CheckGrammar(grammar);

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            var errors = string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"Type checking failed:\n{errors}");
        }

        // 2. Генерация правил
        var generator = new RuleGenerator(globalScope, parser);
        generator.GenerateRules();
    }
}
