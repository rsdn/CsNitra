using CsPreprocessor;
using ExtensibleParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsPreprocessorTests;

[TestClass]
public sealed class PreprocessorDirectiveTests
{
    private static readonly string[] DirectiveKinds = ["Else", "EndIf", "Define", "Undef", "BadDirective"];

    [TestMethod]
    public void DirectiveKinds_AreProduced()
    {
        var source = "int a;\n#define FOO\n#undef FOO\n#else\n#endif\n#bogus\nint b;\n";

        var top = ParseTop(source);
        var lines = top.Elements;

        AssertKinds(lines, "CodeLine", "DirectiveLine", "DirectiveLine", "DirectiveLine", "DirectiveLine", "DirectiveLine", "CodeLine");

        var directives = GetDirectiveLines(lines).Select(GetDirective).ToArray();
        Assert.AreEqual(5, directives.Length);
        Assert.AreEqual("Define", directives[0].Kind);
        Assert.AreEqual("Undef", directives[1].Kind);
        Assert.AreEqual("Else", directives[2].Kind);
        Assert.AreEqual("EndIf", directives[3].Kind);
        Assert.AreEqual("BadDirective", directives[4].Kind);

        Assert.AreEqual("FOO", GetSymbolText(directives[0], source));
        Assert.AreEqual("FOO", GetSymbolText(directives[1], source));
        Assert.IsNull(GetSymbolText(directives[2], source));
        Assert.IsNull(GetSymbolText(directives[3], source));
        Assert.AreEqual("bogus", GetSymbolText(directives[4], source));
    }

    [TestMethod]
    public void Else_DoesNotError()
    {
        var source = "int a;\n#else\nint b;\n";

        var top = ParseTop(source);

        var directive = GetDirective(GetDirectiveLines(top.Elements).Single());
        Assert.AreEqual("Else", directive.Kind);
    }

    [TestMethod]
    public void GluedKeywords_AreBadDirective()
    {
        var source = "int a;\n#defineX\n#elseX\nint b;\n";

        var top = ParseTop(source);
        var directives = GetDirectiveLines(top.Elements).Select(GetDirective).ToArray();

        Assert.AreEqual(2, directives.Length);
        Assert.AreEqual("BadDirective", directives[0].Kind);
        Assert.AreEqual("defineX", GetSymbolText(directives[0], source));
        Assert.AreEqual("BadDirective", directives[1].Kind);
        Assert.AreEqual("elseX", GetSymbolText(directives[1], source));
    }

    [TestMethod]
    public void Tiling_HoldsForAllDirectiveKinds()
    {
        var source = "int a;\n#define FOO\n#undef FOO\n#else\n#endif\n#bogus\n#defineX\n#elseX\nint b;\n";

        var top = ParseTop(source);
        var lines = top.Elements;

        AssertKinds(lines, "CodeLine", "DirectiveLine", "DirectiveLine", "DirectiveLine", "DirectiveLine", "DirectiveLine", "DirectiveLine", "DirectiveLine", "CodeLine");

        Assert.AreEqual(0, top.StartPos);
        Assert.AreEqual(source.Length, top.EndPos);
        for (var i = 1; i < lines.Count; i++)
            Assert.AreEqual(lines[i - 1].EndPos, lines[i].StartPos, $"Line {i} not contiguous with line {i - 1}");
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

    private static IReadOnlyList<SeqNode> GetDirectiveLines(IReadOnlyList<ISyntaxNode> lines) =>
        lines.OfType<SeqNode>().Where(l => l.Kind == "DirectiveLine").ToList();

    private static SeqNode GetDirective(SeqNode directiveLine)
    {
        Assert.AreEqual("DirectiveLine", directiveLine.Kind);
        var directive = directiveLine.Elements.OfType<SeqNode>().FirstOrDefault(s => DirectiveKinds.Contains(s.Kind));
        Assert.IsNotNull(directive, "No directive node found in DirectiveLine");
        return directive!;
    }

    private static string? GetSymbolText(SeqNode directive, string source)
    {
        var symbol = directive.Elements.OfType<TerminalNode>().FirstOrDefault(t => t.Kind == "Symbol");
        return symbol?.ToString(source);
    }

    private static void AssertKinds(IReadOnlyList<ISyntaxNode> lines, params string[] expectedKinds)
    {
        Assert.AreEqual(expectedKinds.Length, lines.Count);
        for (var i = 0; i < expectedKinds.Length; i++)
        {
            var kind = EffectiveKind(lines[i]);
            Assert.AreEqual(expectedKinds[i], kind, $"Line {i} kind is {kind}, expected {expectedKinds[i]}");
        }
    }

    private static string EffectiveKind(ISyntaxNode line) => line switch
    {
        TerminalNode { Kind: "CodeLine" } => "CodeLine",
        SeqNode { Kind: "DirectiveLine" } => "DirectiveLine",
        _ => throw new InvalidOperationException($"Unexpected line node {line.GetType().Name} (Kind={line.Kind})")
    };
}
