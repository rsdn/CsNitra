using CSharpGrammar;
using ExtensibleParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// Helper for T2.3.1 expression tests. Unlike Cs1RoslynTestHelper (which parses a whole
// CompilationUnit via the "Grammar" start rule), this parses a bare Expression fragment via
// the "Expression" start rule — the current C# 1.0 grammar has no statement/declaration that
// embeds an Expression, so the TDOPP Expression rule is exercised directly.
public static class Cs1ExpressionTestHelper
{
    public static CSharpParser CreateParser() =>
        new(
            EmbeddedGrammar.LoadCs1Grammar(),
            CSharpTerminals.Trivia(),
            CSharpTerminals.GetAll());

    public static void AssertParses(string input)
    {
        var parser = CreateParser();
        var result = parser.Parse(input, "Expression", out _);

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
        var result = parser.Parse(input, "Expression", out _);

        Assert.IsFalse(
            result.TryGetSuccess(out _, out var end) && end == input.Length
                && parser.Parser.RecoveryDiagnostics.Count == 0,
            $"Expected parse failure or recovery diagnostics for: {input}");
    }

    // Parses a bare Expression and returns its root node (for tree-shape assertions, e.g. proving
    // that (int) x + y is ((int)x)+y and not (int)(x+y)).
    public static ISyntaxNode ParseExpression(string input)
    {
        var parser = CreateParser();
        var result = parser.Parse(input, "Expression", out _);

        Assert.IsNull(parser.Parser.ErrorInfo);
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end), $"Expected success: {input}");
        Assert.IsNotNull(node);
        Assert.AreEqual(input.Length, end);
        Assert.AreEqual(0, parser.Parser.RecoveryDiagnostics.Count);
        return node;
    }
}
