using CsPreprocessor;
using ExtensibleParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsPreprocessorTests;

[TestClass]
public class PreprocessorGrammarTests
{
    [TestMethod]
    public void MixedLfFile_TilesWholeSource()
    {
        var source = "int a = 1;\n#define FOO\nint b = 2;\n#if FOO\nint c = 3;\n#endif\n";

        var lines = ParseAndTile(source);

        AssertKinds(lines, "CodeLine", "DirectiveLine", "CodeLine", "DirectiveLine", "CodeLine", "DirectiveLine");
    }

    [TestMethod]
    public void CrlfFile_TilesWholeSource()
    {
        var source = "int a = 1;\r\n#define FOO\r\nint b = 2;\r\n#if FOO\r\nint c = 3;\r\n#endif\r\n";

        var lines = ParseAndTile(source);

        AssertKinds(lines, "CodeLine", "DirectiveLine", "CodeLine", "DirectiveLine", "CodeLine", "DirectiveLine");
    }

    [TestMethod]
    public void LastLineWithoutTrailingNewline_TilesWholeSource()
    {
        var source = "int a = 1;\n#define FOO";

        var lines = ParseAndTile(source);

        AssertKinds(lines, "CodeLine", "DirectiveLine");
    }

    [TestMethod]
    public void BlankAndWhitespaceOnlyLines_AreCode()
    {
        var source = "int a = 1;\n\n   \n#define FOO\nint b = 2;\n";

        var lines = ParseAndTile(source);

        AssertKinds(lines, "CodeLine", "CodeLine", "CodeLine", "DirectiveLine", "CodeLine");
    }

    [TestMethod]
    public void Run_EmptySource_ReturnsEmptyText()
    {
        var result = Preprocessor.Run("", Array.Empty<string>());

        Assert.AreEqual("", result.Text);
        Assert.AreEqual(0, result.Diagnostics.Count);
        Assert.AreEqual(0, result.LineDirectives.Count);
    }

    private static string EffectiveKind(ISyntaxNode line) => line switch
    {
        TerminalNode { Kind: "CodeLine" } => "CodeLine",
        SeqNode { Kind: "DirectiveLine" } => "DirectiveLine",
        _ => throw new InvalidOperationException($"Unexpected line node {line.GetType().Name} (Kind={line.Kind})")
    };

    private static List<(string Kind, int Start, int End)> ParseAndTile(string source)
    {
        var parser = Preprocessor.BuildParser();
        var result = parser.Parse(source, "PreprocessorFile", out _);

        Assert.IsTrue(result.IsSuccess, $"Parse failed for source: «{source}»");
        Assert.IsTrue(result.TryGetSuccess(out var top, out var newPos));

        Assert.AreEqual("PreprocessorFile", top.Kind);
        Assert.AreEqual(0, top.StartPos);
        Assert.AreEqual(source.Length, top.EndPos);
        Assert.AreEqual(source.Length, newPos);

        var topSeq = (SeqNode)top;
        var lines = new List<(string Kind, int Start, int End)>();
        foreach (var line in topSeq.Elements)
        {
            lines.Add((EffectiveKind(line), line.StartPos, line.EndPos));
        }

        AssertTiling(lines, source.Length);
        return lines;
    }

    private static void AssertTiling(IReadOnlyList<(string Kind, int Start, int End)> lines, int sourceLength)
    {
        if (lines.Count == 0)
        {
            Assert.AreEqual(0, sourceLength);
            return;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var expectedStart = i == 0 ? 0 : lines[i - 1].End;
            Assert.AreEqual(expectedStart, lines[i].Start, $"Line {i} starts at {lines[i].Start}, expected {expectedStart}");
        }

        Assert.AreEqual(sourceLength, lines[^1].End, $"Last line ends at {lines[^1].End}, expected {sourceLength}");
    }

    private static void AssertKinds(IReadOnlyList<(string Kind, int Start, int End)> lines, params string[] expectedKinds)
    {
        Assert.AreEqual(expectedKinds.Length, lines.Count);
        for (var i = 0; i < expectedKinds.Length; i++)
            Assert.AreEqual(expectedKinds[i], lines[i].Kind, $"Line {i} kind is {lines[i].Kind}, expected {expectedKinds[i]}");
    }
}
