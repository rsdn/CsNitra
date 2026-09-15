using System.Reflection;
using CSharpGrammar;

namespace CSharpGrammarTests;

public static class EmbeddedGrammar
{
    public static string LoadCs1Grammar()
    {
        var assembly = typeof(CSharpParser).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Cs1.grammar", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found in {assembly.FullName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
