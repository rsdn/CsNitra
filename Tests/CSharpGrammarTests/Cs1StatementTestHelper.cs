using CSharpGrammar;
using ExtensibleParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// Helper for T2.2.1 statement tests. Unlike Cs1ExpressionTestHelper (start rule "Expression") and
// Cs1RoslynTestHelper (start rule "Grammar" / whole CompilationUnit), this parses a Block fragment
// via the "Block" start rule — the C# 1.0 grammar has no method body yet (T2.1), so the simple
// statements are exercised inside a Block. The input always includes the outer "{ }".
public static class Cs1StatementTestHelper
{
    public static CSharpParser CreateParser() =>
        new(
            EmbeddedGrammar.LoadCs1Grammar(),
            CSharpTerminals.Trivia(),
            CSharpTerminals.GetAll());

    public static void AssertParses(string input)
    {
        var parser = CreateParser();
        var result = parser.Parse(input, "Block", out _);

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
        var result = parser.Parse(input, "Block", out _);

        Assert.IsFalse(
            result.TryGetSuccess(out _, out var end) && end == input.Length
                && parser.Parser.RecoveryDiagnostics.Count == 0,
            $"Expected parse failure or recovery diagnostics for: {input}");
    }

    // Parses a bare Block and returns its root node (for tree-shape assertions).
    public static ISyntaxNode ParseBlock(string input)
    {
        var parser = CreateParser();
        var result = parser.Parse(input, "Block", out _);

        Assert.IsNull(parser.Parser.ErrorInfo);
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end), $"Expected success: {input}");
        Assert.IsNotNull(node);
        Assert.AreEqual(input.Length, end);
        Assert.AreEqual(0, parser.Parser.RecoveryDiagnostics.Count);
        return node;
    }

    // Returns the Kind of the FIRST statement inside the block. Used to prove the
    // declaration-vs-expression disambiguation (e.g. "LocalVariableDeclaration" vs
    // "ExpressionStatement"). Block = "{" Statement* "}" → Elements = [ { , <ZeroOrMany> , } ].
    public static string FirstStatementKind(string input)
    {
        var block = ParseBlock(input) as SeqNode
            ?? throw new InvalidOperationException($"Expected SeqNode block, got {ParseBlock(input)?.GetType().Name}");
        var statements = block.Elements[1] as SeqNode
            ?? throw new InvalidOperationException($"Expected statements node, got {block.Elements[1].GetType().Name}");
        return statements.Elements[0].Kind;
    }
}
