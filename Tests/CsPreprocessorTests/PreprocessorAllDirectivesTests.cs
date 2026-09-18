using CsPreprocessor;
using ExtensibleParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsPreprocessorTests;

[TestClass]
public sealed class PreprocessorAllDirectivesTests
{
    private static readonly string[] DirectiveKinds =
    [
        "If", "Elif", "Else", "EndIf", "Define", "Undef", "Error", "Warning",
        "LineDir", "Region", "EndRegion", "Pragma", "Nullable", "Shebang", "BadDirective"
    ];

    [TestMethod]
    public void AllDirectiveKinds_ProduceTheRightNodeKind()
    {
        var source =
            "int a;\n" +
            "#if DEBUG\n" +
            "#elif RELEASE\n" +
            "#else\n" +
            "#endif\n" +
            "#define X\n" +
            "#undef X\n" +
            "#error \"boom\"\n" +
            "#warning \"w\"\n" +
            "#line 42 \"Other.cs\"\n" +
            "#region Top\n" +
            "#endregion\n" +
            "#pragma warning disable 0162\n" +
            "#nullable enable\n" +
            "int b;\n";

        var top = ParseTop(source);
        var lines = top.Elements;

        AssertKinds(lines,
            "CodeLine",
            "DirectiveLine", "DirectiveLine", "DirectiveLine", "DirectiveLine",
            "DirectiveLine", "DirectiveLine", "DirectiveLine", "DirectiveLine",
            "DirectiveLine", "DirectiveLine", "DirectiveLine", "DirectiveLine",
            "DirectiveLine",
            "CodeLine");

        AssertDirectiveKinds(top,
            "If", "Elif", "Else", "EndIf", "Define", "Undef", "Error", "Warning",
            "LineDir", "Region", "EndRegion", "Pragma", "Nullable");

        Assert.IsFalse(GetDirectiveKinds(top).Contains("BadDirective"), "A known directive was misclassified as BadDirective");
    }

    [TestMethod]
    public void Shebang_IsRecognized()
    {
        var source = "#!usr/bin/env dotnet\nint x;\n";

        var top = ParseTop(source);
        var lines = top.Elements;

        AssertKinds(lines, "DirectiveLine", "CodeLine");

        var directive = GetDirective(GetDirectiveLines(lines).Single());
        Assert.AreEqual("Shebang", directive.Kind);
    }

    [TestMethod]
    public void GluedKeywords_AreBadDirective()
    {
        var source =
            "int a;\n" +
            "#ifX\n" +
            "#errorX\n" +
            "#lineX\n" +
            "#nullableX\n" +
            "#regionX\n" +
            "int b;\n";

        var top = ParseTop(source);
        var lines = top.Elements;

        AssertKinds(lines,
            "CodeLine",
            "DirectiveLine", "DirectiveLine", "DirectiveLine", "DirectiveLine", "DirectiveLine",
            "CodeLine");

        AssertDirectiveKinds(top, "BadDirective", "BadDirective", "BadDirective", "BadDirective", "BadDirective");
    }

    [TestMethod]
    public void IfWithNoSpace_IsAnIfDirective()
    {
        var source = "int a;\n#if(FOO)\nint b;\n";

        var top = ParseTop(source);
        var lines = top.Elements;

        AssertKinds(lines, "CodeLine", "DirectiveLine", "CodeLine");

        var directive = GetDirective(GetDirectiveLines(lines).Single());
        Assert.AreEqual("If", directive.Kind);
    }

    [TestMethod]
    public void MixedAllDirectives_NoEqualLengthMatchError()
    {
        var source =
            "int a;\n" +
            "#if DEBUG\n" +
            "#elif RELEASE\n" +
            "#else\n" +
            "#endif\n" +
            "#define X\n" +
            "#undef X\n" +
            "#error \"boom\"\n" +
            "#warning \"w\"\n" +
            "#line 42 \"Other.cs\"\n" +
            "#region Top\n" +
            "#endregion\n" +
            "#pragma warning disable 0162\n" +
            "#nullable enable\n" +
            "#if(FOO)\n" +
            "#ifX\n" +
            "#errorX\n" +
            "#lineX\n" +
            "#nullableX\n" +
            "#regionX\n" +
            "int b;\n";

        var top = ParseTop(source);

        AssertDirectiveKinds(top,
            "If", "Elif", "Else", "EndIf", "Define", "Undef", "Error", "Warning",
            "LineDir", "Region", "EndRegion", "Pragma", "Nullable",
            "If", "BadDirective", "BadDirective", "BadDirective", "BadDirective", "BadDirective");
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

    private static string[] GetDirectiveKinds(SeqNode top) =>
        GetDirectiveLines(top.Elements).Select(GetDirective).Select(d => d.Kind).ToArray();

    private static void AssertDirectiveKinds(SeqNode top, params string[] expected)
    {
        var actual = GetDirectiveKinds(top);
        Assert.AreEqual(expected.Length, actual.Length, "Directive count mismatch");
        for (var i = 0; i < expected.Length; i++)
            Assert.AreEqual(expected[i], actual[i], $"Directive {i} kind is {actual[i]}, expected {expected[i]}");
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
