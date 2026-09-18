using CsPreprocessor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsPreprocessorTests;

[TestClass]
public sealed class PositionPreservationTests
{
    // T3.1 — dedicated position-preservation suite (separate from the interpreter-behavior tests).
    //
    // The preprocessor is an interpreter that emits a directive-free file via same-length blanking
    // (D2): directive lines and inactive code lines are blanked to spaces (newlines preserved),
    // active code lines are kept verbatim. The KEY GUARANTEE is the identity/1:1 position mapping:
    // Text.Length == source.Length and a position in Text is the SAME position in source, so a PEG
    // diagnostic reported on Text aligns 1:1 with the original file.
    //
    // Every test below runs Preprocessor.Run and asserts the full 1:1 invariant via AssertOneToOne.
    // The mixed and nested cases additionally do concrete offset spot-checks (a known-kept line is
    // byte-identical at the same offset; a known-blanked line is all-spaces/newlines at the same offset).

    // 1. No directives — pure code, fully kept.
    [TestMethod]
    public void No_Directives_FullyKept()
    {
        const string source = "int a = 1;\nint b = 2;\nvar c = a + b;\n";
        AssertOneToOne(source, Run(source));
    }

    // 2. Active directives only — #define / #if true / #endif around kept code.
    [TestMethod]
    public void Active_Directives_Only_CodeKept()
    {
        const string source = "#define FOO\n#if true\nint kept;\n#endif\n";
        AssertOneToOne(source, Run(source));
    }

    // 3. Inactive region — #if A (A undefined) ... #endif with code inside (blanked) + code outside (kept).
    [TestMethod]
    public void Inactive_Region_Blanked_CodeOutsideKept()
    {
        const string source = "#if A\nint inside;\n#endif\nint outside;\n";
        AssertOneToOne(source, Run(source));
    }

    // 4. Mixed active/inactive — #if A / #else with both branches present (one kept, one blanked).
    [TestMethod]
    public void Mixed_ActiveInactive_ElseBranch()
    {
        const string source = "#if A\nint in_if;\n#else\nint in_else;\n#endif\n";
        var result = Run(source);
        AssertOneToOne(source, result);

        // Concrete spot-checks of the identity mapping at specific offsets.
        AssertLineByteIdentical(source, result.Text, lineIndex: 3); // "int in_else;" kept
        AssertLineBlankedAtOffset(source, result.Text, lineIndex: 1); // "int in_if;" blanked
    }

    // 5. Nested #if — nested blocks, inner inactive (outer active), so both a kept and a blanked code line exist.
    [TestMethod]
    public void Nested_If_InnerInactive()
    {
        const string source = "#define A\n#if A\n#if B\nint inner;\n#endif\nint outer;\n#endif\n";
        var result = Run(source);
        AssertOneToOne(source, result);

        AssertLineByteIdentical(source, result.Text, lineIndex: 5); // "int outer;" kept
        AssertLineBlankedAtOffset(source, result.Text, lineIndex: 3); // "int inner;" blanked
    }

    // 6. #define + #undef driving active/inactive.
    [TestMethod]
    public void Define_Undef_DriveActiveInactive()
    {
        const string source = "#define A\n#if A\nint before;\n#endif\n#undef A\n#if A\nint after;\n#endif\n";
        AssertOneToOne(source, Run(source));
    }

    // 7. #error / #warning / #region / #endregion / #pragma / #nullable / #line lines (directive lines,
    //    blanked) interleaved with code (kept). Diagnostics from #error/#warning are irrelevant here —
    //    only the position mapping of Text is asserted.
    [TestMethod]
    public void Misc_Directives_InterleavedWithCode()
    {
        const string source =
            "int a;\n" +
            "#error \"boom\"\n" +
            "#warning \"w\"\n" +
            "#region Top\n" +
            "int b;\n" +
            "#endregion\n" +
            "#pragma warning disable 0162\n" +
            "#nullable enable\n" +
            "#line 42 \"Other.cs\"\n" +
            "int c;\n";
        AssertOneToOne(source, Run(source));
    }

    // 8. CRLF input — the same logical mixed input with \r\n newlines; both \r and \n are preserved
    //    at the same positions and the length is unchanged.
    [TestMethod]
    public void CRLF_Newlines_PreservedAtSamePositions()
    {
        const string source = "#if A\r\nint in_if;\r\n#else\r\nint in_else;\r\n#endif\r\n";
        var result = Run(source);
        AssertOneToOne(source, result);

        // Explicit: every \r is preserved at the same position (AssertOneToOne already covers \n and \r
        // per-char; this makes the CRLF guarantee explicit).
        for (var i = 0; i < source.Length; i++)
            Assert.IsTrue((source[i] == '\r') == (result.Text[i] == '\r'), $"\r position {i} does not coincide between source and Text");
    }

    // 9. Leading/trailing whitespace and indented directives — "   #if A" (ws before #) and indented code.
    [TestMethod]
    public void Indented_Directives_And_Code()
    {
        const string source = "   #if A\n    int indented;\n   #endif\nint normal;\n";
        AssertOneToOne(source, Run(source));
    }

    private static PreprocessResult Run(string source, params string[] symbols)
        => Preprocessor.Run(source, symbols);

    // The full 1:1 invariant for one input:
    //   - Text.Length == source.Length
    //   - for every i: newline kept at the same position, or blanked to space, or kept verbatim
    //   - newline positions coincide: (source[i]=='\n') == (Text[i]=='\n')
    private static void AssertOneToOne(string source, PreprocessResult result)
    {
        var text = result.Text;
        Assert.AreEqual(source.Length, text.Length, "Text.Length != source.Length (identity mapping broken)");

        for (var i = 0; i < source.Length; i++)
        {
            var s = source[i];
            var t = text[i];

            if (s is '\n' or '\r')
                Assert.IsTrue(t == s, $"newline at offset {i} not preserved (source='{s}' text='{t}')");
            else if (t != ' ')
                Assert.IsTrue(t == s, $"char at offset {i} neither kept nor blanked (source='{s}' text='{t}')");
            // else t == ' ' -> blanked, ok.

            Assert.IsTrue((s == '\n') == (t == '\n'), $"newline position {i} does not coincide between source and Text");
        }
    }

    // A known-kept line is byte-identical at the same offset in both Text and source.
    private static void AssertLineByteIdentical(string source, string text, int lineIndex)
    {
        var (start, end) = LineBounds(source, lineIndex);
        Assert.AreEqual(
            source.Substring(start, end - start),
            text.Substring(start, end - start),
            $"line {lineIndex} @ offset {start} is not byte-identical between source and Text");
    }

    // A known-blanked line is all-spaces (except newlines) at the same offset, and the source line
    // actually had blankable content (so the check is not vacuous).
    private static void AssertLineBlankedAtOffset(string source, string text, int lineIndex)
    {
        var (start, end) = LineBounds(source, lineIndex);
        var slice = text.Substring(start, end - start);
        Assert.IsTrue(slice.All(c => c is ' ' or '\n' or '\r'), $"line {lineIndex} @ offset {start} not blanked (expected all spaces/newlines): «{slice}»");

        var srcSlice = source.Substring(start, end - start);
        Assert.IsTrue(srcSlice.Any(c => c is not '\n' and not '\r'), $"line {lineIndex} had no blankable content; spot-check is vacuous");
    }

    // [start, end) byte range of the lineIndex-th line, including its trailing '\n' when present.
    private static (int Start, int End) LineBounds(string source, int lineIndex)
    {
        var start = 0;
        for (var i = 0; i < lineIndex; i++)
        {
            var next = source.IndexOf('\n', start);
            Assert.IsTrue(next >= 0, $"source has fewer than {lineIndex + 1} lines");
            start = next + 1;
        }

        var nl = source.IndexOf('\n', start);
        var end = nl >= 0 ? nl + 1 : source.Length;
        return (start, end);
    }
}
