using CsPreprocessor;
using ExtensibleParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsPreprocessorTests;

[TestClass]
public sealed class PreprocessorConditionTests
{
    // Every condition expression exercised by the shape tests (also reused by the tiling test).
    private static readonly string[] ConditionSources =
    [
        "FOO",
        "!A",
        "A && B",
        "A || B",
        "A || B && C",
        "A && B || C",
        "A == B",
        "A != B",
        "(A || B) && C",
        "!A && B || C == D",
        "true",
        "A&&B",
    ];

    [TestMethod]
    public void SingleIdentifier_IsASymbolWithNoOperatorNode()
    {
        var condition = GetConditionNode("#if FOO\n");

        Assert.AreEqual("Symbol", condition.Kind);
        Assert.AreEqual("FOO", condition.ToString("#if FOO\n"));
        Assert.AreEqual("FOO", DescribeCondition("#if FOO\n"));
    }

    [TestMethod]
    public void UnaryNot_IsCondNot()
    {
        Assert.AreEqual("CondNot(A)", DescribeCondition("#if !A\n"));
    }

    [TestMethod]
    public void And_IsCondAnd()
    {
        Assert.AreEqual("CondAnd(A, B)", DescribeCondition("#if A && B\n"));
    }

    [TestMethod]
    public void Or_IsCondOr()
    {
        Assert.AreEqual("CondOr(A, B)", DescribeCondition("#if A || B\n"));
    }

    [TestMethod]
    public void And_BindsTighterThanOr()
    {
        Assert.AreEqual("CondOr(A, CondAnd(B, C))", DescribeCondition("#if A || B && C\n"));
    }

    [TestMethod]
    public void Or_GroupsLoosely()
    {
        Assert.AreEqual("CondOr(CondAnd(A, B), C)", DescribeCondition("#if A && B || C\n"));
    }

    [TestMethod]
    public void Equality_IsCondEq()
    {
        Assert.AreEqual("CondEq(A, B)", DescribeCondition("#if A == B\n"));
    }

    [TestMethod]
    public void Inequality_IsCondNotEq()
    {
        Assert.AreEqual("CondNotEq(A, B)", DescribeCondition("#if A != B\n"));
    }

    [TestMethod]
    public void Parens_OverridePrecedence()
    {
        Assert.AreEqual("CondAnd(Paren(CondOr(A, B)), C)", DescribeCondition("#if (A || B) && C\n"));
    }

    [TestMethod]
    public void FullChain_CorrectPrecedenceAndAssociativity()
    {
        Assert.AreEqual("CondOr(CondAnd(CondNot(A), B), CondEq(C, D))", DescribeCondition("#if !A && B || C == D\n"));
    }

    [TestMethod]
    public void True_IsASymbol()
    {
        var condition = GetConditionNode("#if true\n");

        Assert.AreEqual("Symbol", condition.Kind);
        Assert.AreEqual("true", condition.ToString("#if true\n"));
    }

    [TestMethod]
    public void WhitespaceIsInsensitive_ProducesTheSameShape()
    {
        var noSpace = DescribeCondition("#if A&&B\n");
        var spaced = DescribeCondition("#if A && B\n");

        Assert.AreEqual("CondAnd(A, B)", noSpace);
        Assert.AreEqual(noSpace, spaced);
    }

    [TestMethod]
    public void Tiling_HoldsForAllConditions()
    {
        foreach (var cond in ConditionSources)
        {
            var source = $"int a;\n#if {cond}\nint b;\n";

            var top = ParseTop(source);

            // ParseTop already asserts top == [0, source.Length) and that the lines tile contiguously.
            Assert.AreEqual(3, top.Elements.Count, $"Expected 3 lines (code, directive, code) for condition «{cond}»");
        }
    }

    private static SeqNode ParseTop(string source)
    {
        var parser = Preprocessor.BuildParser();
        var result = parser.Parse(source, "PreprocessorFile", out _);

        Assert.IsTrue(result.IsSuccess, $"Parse failed for source: «{source}»");
        Assert.IsTrue(result.TryGetSuccess(out var top, out var newPos), "Parse did not succeed");

        Assert.AreEqual("PreprocessorFile", top.Kind);
        Assert.AreEqual(0, top.StartPos);
        Assert.AreEqual(source.Length, top.EndPos);
        Assert.AreEqual(source.Length, newPos);

        var topSeq = (SeqNode)top;
        AssertTiling(topSeq.Elements, source.Length);
        return topSeq;
    }

    private static void AssertTiling(IReadOnlyList<ISyntaxNode> lines, int sourceLength)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var expectedStart = i == 0 ? 0 : lines[i - 1].EndPos;
            Assert.AreEqual(expectedStart, lines[i].StartPos, $"Line {i} starts at {lines[i].StartPos}, expected {expectedStart}");
        }

        if (lines.Count > 0)
            Assert.AreEqual(sourceLength, lines[^1].EndPos, $"Last line ends at {lines[^1].EndPos}, expected {sourceLength}");
    }

    private static SeqNode GetIfNode(SeqNode top)
    {
        var ifNode = top.Elements
            .OfType<SeqNode>()
            .Where(l => l.Kind == "DirectiveLine")
            .SelectMany(d => d.Elements)
            .OfType<SeqNode>()
            .FirstOrDefault(s => s.Kind == "If");

        Assert.IsNotNull(ifNode, "No If node found in any directive line");
        return ifNode!;
    }

    private static ISyntaxNode GetCondition(SeqNode ifNode)
    {
        var condition = ifNode.Elements
            .Where(e => e.Kind is not ("If" or "Ws" or "LineEnd"))
            .ToList();

        Assert.AreEqual(1, condition.Count, $"Expected exactly one condition child in If, found: {string.Join(", ", condition.Select(c => c.Kind))}");
        return condition[0];
    }

    private static ISyntaxNode GetConditionNode(string source)
    {
        var top = ParseTop(source);
        return GetCondition(GetIfNode(top));
    }

    private static string DescribeCondition(string source)
    {
        var top = ParseTop(source);
        return Describe(GetCondition(GetIfNode(top)), source);
    }

    private static string Describe(ISyntaxNode node, string source)
    {
        if (node is TerminalNode { Kind: "Symbol" } symbol)
            return symbol.ToString(source);

        if (node is not SeqNode seq)
            throw new InvalidOperationException($"Unexpected condition node: {node.GetType().Name} (Kind={node.Kind})");

        var nonWs = seq.Elements.Where(e => e.Kind != "Ws").ToList();
        return seq.Kind switch
        {
            "Paren" => $"Paren({Describe(nonWs.Single(e => e.Kind is not ("OpenParen" or "CloseParen")), source)})",
            "CondNot" => $"CondNot({Describe(nonWs[^1], source)})",
            "CondAnd" or "CondOr" or "CondEq" or "CondNotEq" => $"{seq.Kind}({Describe(nonWs[0], source)}, {Describe(nonWs[^1], source)})",
            _ => throw new InvalidOperationException($"Unexpected condition node Kind: {seq.Kind}")
        };
    }
}
