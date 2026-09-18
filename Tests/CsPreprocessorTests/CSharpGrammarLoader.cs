using System.Reflection;
using CSharpGrammar;

namespace CsPreprocessorTests;

// T3.2 — loads the C# grammar (embedded resource of the CSharpGrammar assembly) and builds a
// CSharpParser. The CsPreprocessorTests project does NOT reference CSharpGrammarTests, so the
// EmbeddedGrammar helper there is unavailable; this loads the same embedded .grammar resource via
// reflection instead. Use Cs1.grammar (a class/field/method is C# 1.0); bump the suffix only if a
// test needs newer syntax.
public static class CSharpGrammarLoader
{
    public static CSharpParser CreateCSharpParser()
    {
        var assembly = typeof(CSharpParser).Assembly;
        var text = Load(assembly, "Cs1.grammar");
        var grammars = new List<(string, string)> { (text, "Cs1.grammar") };
        return new CSharpParser(grammars, CSharpTerminals.Trivia(), CSharpTerminals.GetAll());
    }

    private static string Load(Assembly assembly, string resourceSuffix)
    {
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(resourceSuffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceSuffix}' not found");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
