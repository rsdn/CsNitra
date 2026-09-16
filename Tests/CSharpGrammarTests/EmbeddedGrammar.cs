using System.Reflection;
using CSharpGrammar;

namespace CSharpGrammarTests;

public static class EmbeddedGrammar
{
    public static string LoadCs1Grammar() => Load("Cs1.grammar");

    public static string LoadCs6Grammar() => Load("Cs6.grammar");

    public static string LoadCs11Grammar() => Load("Cs11.grammar");

    private static string Load(string resourceSuffix)
    {
        var assembly = typeof(CSharpParser).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(n => n.EndsWith(resourceSuffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found in {assembly.FullName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
