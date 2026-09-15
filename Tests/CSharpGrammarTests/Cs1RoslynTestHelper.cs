using CSharpGrammar;
using ExtensibleParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

public static class Cs1RoslynTestHelper
{
    public static CSharpParser CreateParser() =>
        new(
            EmbeddedGrammar.LoadCs1Grammar(),
            CSharpTerminals.Trivia(),
            CSharpTerminals.GetAll());

    public static void AssertParses(string input)
    {
        var parser = CreateParser();
        var result = parser.Parse(input, "Grammar", out _);

        Assert.IsNull(parser.Parser.ErrorInfo);
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end),
            $"Expected success, error at pos {parser.Parser.ErrorPos}: {input}");
        Assert.IsNotNull(node);
        Assert.AreEqual(input.Length, end);
        Assert.AreEqual(0, parser.Parser.RecoveryDiagnostics.Count);
    }

    public static void AssertFails(string input)
    {
        var parser = CreateParser();
        var result = parser.Parse(input, "Grammar", out _);

        Assert.IsFalse(
            result.TryGetSuccess(out _, out var end) && end == input.Length
                && parser.Parser.RecoveryDiagnostics.Count == 0,
            $"Expected parse failure or recovery diagnostics for: {input}");
    }
}
