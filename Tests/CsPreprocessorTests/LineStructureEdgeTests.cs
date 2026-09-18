using CsPreprocessor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsPreprocessorTests;

// T4.5 — line-structure edge cases of the preprocessor:
//   * Shebang '#!' at offset 0 (blanked, no symbol effect, no structural diagnostic).
//   * CRLF vs LF line endings (same-length blanking D2 holds; newlines preserved).
//   * Whitespace before '#' (a line like '   #if A' is a directive).
//   * '#' after a token in a line (bad placement => CODE, not a directive).
//
// Every test runs Preprocessor.Run and asserts the same-length invariant
// (Text.Length == source.Length) plus concrete per-line blanking/keeping.
[TestClass]
public sealed class LineStructureEdgeTests
{
    // 1. Shebang at offset 0 — the '#!...' line is a shebang directive (blanked, no symbol effect,
    //    no structural diagnostic); the following code line is kept verbatim.
    [TestMethod]
    public void Shebang_AtOffset0_Blanked_CodeKept()
    {
        const string source = "#!/usr/bin/env csi\nint x;\n";
        var result = Run(source);

        AssertSameLength(source, result);
        AssertLineBlanked(source, result.Text, lineIndex: 0); // '#!/usr/bin/env csi'
        AssertLineKept(source, result.Text, lineIndex: 1); // 'int x;'
        Assert.AreEqual(0, result.Diagnostics.Count);
    }

    // 2. Shebang does not define symbols — '#!define FOO' is a shebang (NOT a '#define'), so FOO is
    //    not defined and the '#if FOO' region is inactive => 'int x;' is blanked.
    [TestMethod]
    public void Shebang_DoesNotDefineSymbols_RegionInactive()
    {
        const string source = "#!define FOO\n#if FOO\nint x;\n#endif\n";
        var result = Run(source);

        AssertSameLength(source, result);
        AssertLineBlanked(source, result.Text, lineIndex: 0); // shebang
        AssertLineBlanked(source, result.Text, lineIndex: 2); // 'int x;' inactive
        Assert.AreEqual(0, result.Diagnostics.Count);
    }

    // 3. CRLF line endings — both '\r' and '\n' are preserved at the same positions; the active code
    //    line is kept verbatim; length unchanged; no diagnostics.
    [TestMethod]
    public void CRLF_Newlines_Preserved()
    {
        const string source = "#define FOO\r\n#if FOO\r\nint x;\r\n#endif\r\n";
        var result = Run(source);

        AssertSameLength(source, result);
        AssertNewlinesPreserved(source, result.Text);
        AssertLineKept(source, result.Text, lineIndex: 2); // 'int x;'
        Assert.AreEqual(0, result.Diagnostics.Count);
    }

    // 4. Mixed CRLF and LF — each line keeps its own ending ('\r\n' or '\n') at the same position;
    //    both code lines are kept verbatim; length unchanged.
    [TestMethod]
    public void Mixed_CRLF_And_LF_NewlinesPreserved()
    {
        const string source = "#define FOO\r\nint a;\nint b;\r\n";
        var result = Run(source);

        AssertSameLength(source, result);
        AssertNewlinesPreserved(source, result.Text);
        AssertLineKept(source, result.Text, lineIndex: 1); // 'int a;'
        AssertLineKept(source, result.Text, lineIndex: 2); // 'int b;'
    }

    // 5. Whitespace before '#' — a line like '   #if A' (leading ws then '#') is a directive, so FOO
    //    is defined and 'int x;' is kept verbatim; the indented directive lines are blanked.
    [TestMethod]
    public void Whitespace_BeforeHash_IsDirective()
    {
        const string source = "   #define FOO\n   #if FOO\nint x;\n   #endif\n";
        var result = Run(source);

        AssertSameLength(source, result);
        AssertLineBlanked(source, result.Text, lineIndex: 0); // '   #define FOO'
        AssertLineBlanked(source, result.Text, lineIndex: 1); // '   #if FOO'
        AssertLineKept(source, result.Text, lineIndex: 2); // 'int x;'
        AssertLineBlanked(source, result.Text, lineIndex: 3); // '   #endif'
        Assert.AreEqual(0, result.Diagnostics.Count);
    }

    // 6. '#' after a token is not a directive (bad placement) — 'int x; #define FOO' is a CODE line
    //    (the '#' is not at line start), so FOO is NOT defined and 'int y;' is blanked. The '#define'
    //    on the code line does NOT take effect.
    [TestMethod]
    public void Hash_AfterToken_IsCode_NotDirective()
    {
        const string source = "int x; #define FOO\n#if FOO\nint y;\n#endif\n";
        var result = Run(source);

        AssertSameLength(source, result);
        AssertLineKept(source, result.Text, lineIndex: 0); // 'int x; #define FOO' kept verbatim
        AssertLineBlanked(source, result.Text, lineIndex: 2); // 'int y;' inactive
        Assert.AreEqual(0, result.Diagnostics.Count);
    }

    // 7. Same-length invariant across all of inputs 1-6.
    [TestMethod]
    public void SameLength_Invariant_AcrossAllInputs()
    {
        var inputs = new[]
        {
            "#!/usr/bin/env csi\nint x;\n",
            "#!define FOO\n#if FOO\nint x;\n#endif\n",
            "#define FOO\r\n#if FOO\r\nint x;\r\n#endif\r\n",
            "#define FOO\r\nint a;\nint b;\r\n",
            "   #define FOO\n   #if FOO\nint x;\n   #endif\n",
            "int x; #define FOO\n#if FOO\nint y;\n#endif\n"
        };

        foreach (var source in inputs)
            AssertSameLength(source, Run(source));
    }

    private static PreprocessResult Run(string source, params string[] symbols)
        => Preprocessor.Run(source, symbols);

    private static void AssertSameLength(string source, PreprocessResult result)
        => Assert.AreEqual(source.Length, result.Text.Length, "Text.Length != source.Length (same-length invariant broken)");

    // For every position, the '\r' and '\n' characters coincide between source and Text.
    private static void AssertNewlinesPreserved(string source, string text)
    {
        Assert.AreEqual(source.Length, text.Length, "lengths differ; cannot compare newline positions");
        for (var i = 0; i < source.Length; i++)
        {
            Assert.IsTrue((source[i] == '\r') == (text[i] == '\r'), $"\r position {i} does not coincide between source and Text");
            Assert.IsTrue((source[i] == '\n') == (text[i] == '\n'), $"\n position {i} does not coincide between source and Text");
        }
    }

    // A kept line is byte-identical at the same offset in both Text and source.
    private static void AssertLineKept(string source, string text, int lineIndex)
    {
        var (start, end) = LineBounds(source, lineIndex);
        Assert.AreEqual(
            source.Substring(start, end - start),
            text.Substring(start, end - start),
            $"line {lineIndex} @ offset {start} is not byte-identical between source and Text");
    }

    // A blanked line is all ' '/'\n'/'\r' at the same offset, and the source line actually had
    // blankable content (so the check is not vacuous).
    private static void AssertLineBlanked(string source, string text, int lineIndex)
    {
        var (start, end) = LineBounds(source, lineIndex);
        var slice = text.Substring(start, end - start);
        Assert.IsTrue(slice.All(c => c is ' ' or '\n' or '\r'), $"line {lineIndex} @ offset {start} not blanked (expected all spaces/newlines): «{slice}»");

        var srcSlice = source.Substring(start, end - start);
        Assert.IsTrue(srcSlice.Any(c => c is not '\n' and not '\r'), $"line {lineIndex} had no blankable content; check is vacuous");
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
