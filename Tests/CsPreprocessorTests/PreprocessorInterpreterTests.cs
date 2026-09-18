using CsPreprocessor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsPreprocessorTests;

[TestClass]
public sealed class PreprocessorInterpreterTests
{
    // T2.2: Preprocessor.Run builds the preprocessed Text using same-length blanking (D2).
    // Directive lines and inactive code lines are blanked to spaces (newlines preserved);
    // active code lines are kept verbatim. All sources below use LF newlines.

    [TestMethod]
    public void No_Directives_Passthrough()
    {
        const string source = "int x = 1;\n";

        var result = Run(source);

        Assert.AreEqual(source, result.Text);
    }

    [TestMethod]
    public void Directive_Line_Blanked_ActiveCode_Kept()
    {
        const string source = "#define FOO\nint a;\n";

        var result = Run(source);

        AssertLineBlanked(source, result.Text, lineIndex: 0);
        AssertLineKept(source, result.Text, lineIndex: 1);
        SameLength(source, result);
    }

    [TestMethod]
    public void If_True_KeepsBody()
    {
        const string source = "#define A\n#if A\nint keep;\n#endif\n";

        var result = Run(source);

        AssertLineKept(source, result.Text, lineIndex: 2);
    }

    [TestMethod]
    public void If_False_BlanksBody()
    {
        const string source = "#if A\nint drop;\n#endif\n";

        var result = Run(source);

        AssertLineBlanked(source, result.Text, lineIndex: 1);
    }

    [TestMethod]
    public void Define_Drives_If_SymbolState()
    {
        const string source = "#if A\nint x;\n#endif\n#define A\n#if A\nint y;\n#endif\n";

        var result = Run(source);

        // A is undefined at the first #if -> int x; blanked; A is defined by the second #if -> int y; kept.
        AssertLineBlanked(source, result.Text, lineIndex: 1);
        AssertLineKept(source, result.Text, lineIndex: 5);
    }

    [TestMethod]
    public void CommandLine_Symbols()
    {
        const string source = "#if DEBUG\nint x;\n#endif\n";

        var withDebug = Run(source, "DEBUG");
        AssertLineKept(source, withDebug.Text, lineIndex: 1);

        var withoutDebug = Run(source);
        AssertLineBlanked(source, withoutDebug.Text, lineIndex: 1);
    }

    [TestMethod]
    public void Else_FirstTrueBranchWins()
    {
        const string undefined = "#if A\nint x;\n#else\nint y;\n#endif\n";

        var resultA = Run(undefined);
        AssertLineBlanked(undefined, resultA.Text, lineIndex: 1);
        AssertLineKept(undefined, resultA.Text, lineIndex: 3);

        const string defined = "#define A\n#if A\nint x;\n#else\nint y;\n#endif\n";

        var resultB = Run(defined);
        AssertLineKept(defined, resultB.Text, lineIndex: 2);
        AssertLineBlanked(defined, resultB.Text, lineIndex: 4);
    }

    [TestMethod]
    public void Nested_If()
    {
        const string defined = "#define A\n#if A\n#if A\nint x;\n#endif\n#endif\n";

        var resultA = Run(defined);
        AssertLineKept(defined, resultA.Text, lineIndex: 3);

        const string undefined = "#if A\n#if A\nint x;\n#endif\n#endif\n";

        var resultB = Run(undefined);
        AssertLineBlanked(undefined, resultB.Text, lineIndex: 2);
    }

    [TestMethod]
    public void Undef_RemovesSymbol()
    {
        const string source = "#define A\n#undef A\n#if A\nint x;\n#endif\n";

        var result = Run(source);

        AssertLineBlanked(source, result.Text, lineIndex: 3);
    }

    [TestMethod]
    public void SameLength_And_NewlinePreservation()
    {
        var cases = new (string Source, string[] Symbols)[]
        {
            ("#define FOO\nint a;\n", []),
            ("#define A\n#if A\nint keep;\n#endif\n", []),
            ("#if A\nint drop;\n#endif\n", []),
            ("#if A\nint x;\n#endif\n#define A\n#if A\nint y;\n#endif\n", []),
            ("#if DEBUG\nint x;\n#endif\n", ["DEBUG"]),
            ("#if DEBUG\nint x;\n#endif\n", []),
            ("#if A\nint x;\n#else\nint y;\n#endif\n", []),
            ("#define A\n#if A\nint x;\n#else\nint y;\n#endif\n", []),
            ("#define A\n#if A\n#if A\nint x;\n#endif\n#endif\n", []),
            ("#if A\n#if A\nint x;\n#endif\n#endif\n", []),
            ("#define A\n#undef A\n#if A\nint x;\n#endif\n", []),
        };

        foreach (var (source, symbols) in cases)
            SameLength(source, Run(source, symbols));
    }

    [TestMethod]
    public void Blanked_Chars_AreSpaces()
    {
        var cases = new (string Source, string[] Symbols, int Line)[]
        {
            ("#define FOO\nint a;\n", [], 0),
            ("#if A\nint drop;\n#endif\n", [], 1),
            ("#if A\nint x;\n#endif\n#define A\n#if A\nint y;\n#endif\n", [], 1),
            ("#define A\n#undef A\n#if A\nint x;\n#endif\n", [], 3),
        };

        foreach (var (source, symbols, lineIndex) in cases)
            AssertLineBlanked(source, Run(source, symbols).Text, lineIndex);
    }

    private static PreprocessResult Run(string source, params string[] symbols)
        => Preprocessor.Run(source, symbols);

    private static void SameLength(string source, PreprocessResult result)
    {
        Assert.AreEqual(source.Length, result.Text.Length, "Text.Length != source.Length");

        var sourceNewlines = source.Count(c => c == '\n');
        var textNewlines = result.Text.Count(c => c == '\n');
        Assert.AreEqual(sourceNewlines, textNewlines, "newline count differs between source and Text");
    }

    private static void AssertLineKept(string source, string text, int lineIndex)
    {
        var src = source.Split('\n');
        var txt = text.Split('\n');

        Assert.AreEqual(src.Length, txt.Length, "line count differs between source and Text");
        Assert.AreEqual(src[lineIndex], txt[lineIndex], $"line {lineIndex} not kept verbatim");
    }

    private static void AssertLineBlanked(string source, string text, int lineIndex)
    {
        var src = source.Split('\n');
        var txt = text.Split('\n');

        Assert.AreEqual(src.Length, txt.Length, "line count differs between source and Text");
        Assert.AreEqual(src[lineIndex].Length, txt[lineIndex].Length, $"line {lineIndex} length differs");
        Assert.IsTrue(txt[lineIndex].All(c => c == ' '), $"line {lineIndex} is not all spaces: «{txt[lineIndex]}»");
    }
}
