using System.Reflection;
using CSharpGrammar;

namespace CSharpGrammarTests;

public static class EmbeddedGrammar
{
    // version → (embedded-resource suffix, display path). Ascending order. Add a new version
    // by inserting one line (plus the .grammar file + its <EmbeddedResource> entry when that
    // grammar file itself is added in a later Stage-3 task).
    private static readonly IReadOnlyList<GrammarVersion> _versions =
    [
        new(1,  "Cs1.grammar",  "Cs1.grammar"),
        new(2,  "Cs2.grammar",  "Cs2.grammar"),
        new(3,  "Cs3.grammar",  "Cs3.grammar"),
        new(4,  "Cs4.grammar",  "Cs4.grammar"),
        new(5,  "Cs5.grammar",  "Cs5.grammar"),
        new(6,  "Cs6.grammar",  "Cs6.grammar"),
        new(7,  "Cs7.grammar",  "Cs7.grammar"),
        new(8,  "Cs8.grammar",  "Cs8.grammar"),
        new(9,  "Cs9.grammar",  "Cs9.grammar"),
        new(10, "Cs10.grammar", "Cs10.grammar"),
        new(11, "Cs11.grammar", "Cs11.grammar"),
        new(12, "Cs12.grammar", "Cs12.grammar"),
    ];

    public static string LoadCs1Grammar() => Load("Cs1.grammar");

    public static string LoadCs6Grammar() => Load("Cs6.grammar");

    public static string LoadCs11Grammar() => Load("Cs11.grammar");

    public static IReadOnlyList<(string Text, string Path)> LoadGrammarUpTo(int version)
    {
        var selected = _versions
            .Where(v => v.Version <= version)
            .OrderBy(v => v.Version)
            .Select(v => (Text: Load(v.Suffix), Path: v.Path))
            .ToArray();

        if (selected.Length == 0)
            throw new ArgumentException($"No grammar files for version <= {version}", nameof(version));

        return selected;
    }

    private static string Load(string resourceSuffix)
    {
        var assembly = typeof(CSharpParser).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(n => n.EndsWith(resourceSuffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found in {assembly.FullName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record GrammarVersion(int Version, string Suffix, string Path);
}
