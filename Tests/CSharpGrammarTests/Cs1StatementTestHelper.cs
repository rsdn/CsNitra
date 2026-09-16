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
    public static string FirstStatementKind(string input) => FirstStatementNode(input).Kind;

    // Returns the FIRST statement node inside the block (for tree-shape assertions, e.g. the
    // dangling-else binding). Block = "{" Statement* "}" → Elements = [ { , <ZeroOrMany> , } ].
    public static ISyntaxNode FirstStatementNode(string input)
    {
        var block = ParseBlock(input) as SeqNode
            ?? throw new InvalidOperationException($"Expected SeqNode block, got {ParseBlock(input)?.GetType().Name}");
        var statements = block.Elements[1] as SeqNode
            ?? throw new InvalidOperationException($"Expected statements node, got {block.Elements[1].GetType().Name}");
        return statements.Elements[0];
    }

    // IfStatement = "if" "(" Expression ")" Statement ("else" Statement)? → SeqNode Elements:
    // [0]if [1]( [2]expr [3]) [4]then-Statement [5]else-Optional. Returns the then-Statement Kind.
    public static string IfThenKind(SeqNode ifStatement) => ifStatement.Elements[4].Kind;

    // Returns "some" if the IfStatement carries an else (Elements[5] is a SomeNode), else "none".
    public static string IfElsePresent(SeqNode ifStatement) =>
        ifStatement.Elements[5] is SomeNode ? "some" : "none";

    // UsingStatement = "using" "(" ResourceAcquisition ")" Statement → SeqNode Elements:
    // [0]using [1]( [2]ResourceAcquisition [3]) [4]Statement. Returns the ResourceAcquisition Kind
    // ("ResourceDeclaration" vs "Expression") — the using decl-vs-expr disambiguation (T2.2.3).
    public static string UsingResourceKind(string input)
    {
        var usingStatement = FirstStatementNode(input) as SeqNode
            ?? throw new InvalidOperationException($"Expected UsingStatement SeqNode, got {FirstStatementNode(input)?.GetType().Name}");
        return usingStatement.Elements[2].Kind;
    }

    // SwitchStatement = "switch" "(" Expression ")" "{" SwitchSection* "}" → SeqNode Elements:
    // [0]switch [1]( [2]expr [3]) [4]{ [5]SwitchSection* [6]}. Returns the number of sections.
    public static int SwitchSectionCount(string input)
    {
        var switchStatement = FirstStatementNode(input) as SeqNode
            ?? throw new InvalidOperationException($"Expected SwitchStatement SeqNode, got {FirstStatementNode(input)?.GetType().Name}");
        var sections = switchStatement.Elements[5] as SeqNode
            ?? throw new InvalidOperationException($"Expected sections node, got {switchStatement.Elements[5].GetType().Name}");
        return sections.Elements.Count;
    }

    // SwitchSection = SwitchLabel+ Statement* → SeqNode Elements: [0]SwitchLabel+ [1]Statement*.
    // Returns the number of labels in the FIRST section (stacked-labels check: `case 1: case 2:` = 2).
    public static int SwitchFirstSectionLabelCount(string input)
    {
        var switchStatement = FirstStatementNode(input) as SeqNode
            ?? throw new InvalidOperationException($"Expected SwitchStatement SeqNode, got {FirstStatementNode(input)?.GetType().Name}");
        var sections = switchStatement.Elements[5] as SeqNode
            ?? throw new InvalidOperationException($"Expected sections node, got {switchStatement.Elements[5].GetType().Name}");
        var section = sections.Elements[0] as SeqNode
            ?? throw new InvalidOperationException($"Expected section node, got {sections.Elements[0].GetType().Name}");
        var labels = section.Elements[0] as SeqNode
            ?? throw new InvalidOperationException($"Expected labels node, got {section.Elements[0].GetType().Name}");
        return labels.Elements.Count;
    }
}
