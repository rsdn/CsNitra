using CsPreprocessor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsPreprocessorTests;

// T4.4.2: A '#' inside a comment is NOT a directive. The preprocessor reuses the main C#
// parser's trivia (PreprocessorTerminals.Trivia wraps CSharpTerminals.Trivia), so a '#' inside
// a /* ... */ block comment (multi-line) or a // line comment is not seen as a directive-start.
// A #define inside a comment therefore does NOT define the symbol, so a later #if on that symbol
// is inactive and its body is blanked.
[TestClass]
public sealed class CommentDirectiveTests
{
    [TestMethod]
    public void DefineInsideBlockComment_DoesNotDefine()
    {
        const string source = "/* c\n#define FOO\n*/\n#if FOO\nint x;\n#endif\n";

        var result = Run(source);

        // FOO is NOT defined (the #define is inside the multi-line comment), so the #if FOO body
        // is blanked.
        AssertBlanked(result, source, "int x;");
        Assert.AreEqual(0, result.Diagnostics.Count);
        AssertSameLength(result, source);
    }

    [TestMethod]
    public void HashInsideLineComment_IsNotDirective()
    {
        const string source = "// #define FOO\nint y;\n";

        var result = Run(source);

        // The '// #define FOO' line is a comment, not a directive, so FOO is NOT defined. The
        // following code is kept verbatim (there is no #if, so it is active).
        AssertKept(result, source, "int y;");
        Assert.AreEqual(0, result.Diagnostics.Count);
        AssertSameLength(result, source);
    }

    [TestMethod]
    public void CodeAfterMultiLineComment_IsKept()
    {
        const string source = "/* a\nb\n*/\nint z;\n";

        var result = Run(source);

        AssertKept(result, source, "int z;");
        Assert.AreEqual(0, result.Diagnostics.Count);
        AssertSameLength(result, source);
    }

    [TestMethod]
    public void CommentClosesSameLine_ThenRealDirective_Defines()
    {
        const string source = "/* a */\n#define FOO\n#if FOO\nint x;\n#endif\n";

        var result = Run(source);

        // The #define FOO is on its own line after the (closed) comment, so it IS a real
        // directive: FOO is defined and the #if FOO body is kept.
        AssertKept(result, source, "int x;");
        Assert.AreEqual(0, result.Diagnostics.Count);
        AssertSameLength(result, source);
    }

    [TestMethod]
    public void NestedBlockComment_HashInside_DoesNotDefine()
    {
        // The main parser's trivia NESTS block comments (a depth counter: '/*' increments, '*/'
        // decrements), unlike standard C# where '/* */' does not nest. So the whole first line is
        // ONE comment and the '#define FOO' is inside it. FOO is therefore NOT defined and the
        // #if FOO body is blanked.
        const string source = "/* outer /* inner */ #define FOO */\n#if FOO\nint x;\n#endif\n";

        var result = Run(source);

        AssertBlanked(result, source, "int x;");
        Assert.AreEqual(0, result.Diagnostics.Count);
        AssertSameLength(result, source);
    }

    [TestMethod]
    public void HashOnOwnLine_NotInComment_IsDirective()
    {
        const string source = "#define FOO\n#if FOO\nint x;\n#endif\n";

        var result = Run(source);

        // Contrast: a '#' on its own line that is NOT in a comment IS a directive, so FOO is
        // defined and the #if FOO body is kept.
        AssertKept(result, source, "int x;");
        Assert.AreEqual(0, result.Diagnostics.Count);
        AssertSameLength(result, source);
    }

    private static PreprocessResult Run(string source, params string[] symbols)
        => Preprocessor.Run(source, symbols);

    private static void AssertSameLength(PreprocessResult result, string source)
        => Assert.AreEqual(source.Length, result.Text.Length, "Output length changed (D2 violated)");

    private static void AssertBlanked(PreprocessResult result, string source, string marker)
    {
        var (start, end) = LineSpan(source, marker);
        for (var i = start; i < end; i++)
            Assert.IsTrue(
                result.Text[i] is ' ' or '\n' or '\r',
                $"Line «{marker}» is not blanked: unexpected '{result.Text[i]}' (U+{(int)result.Text[i]:X4}) at offset {i}");
    }

    private static void AssertKept(PreprocessResult result, string source, string marker)
    {
        var (start, end) = LineSpan(source, marker);
        Assert.AreEqual(
            source.Substring(start, end - start),
            result.Text.Substring(start, end - start),
            $"Line «{marker}» was not kept verbatim");
    }

    private static (int Start, int End) LineSpan(string source, string marker)
    {
        var markerIdx = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.IsTrue(markerIdx >= 0, $"marker «{marker}» not found in source");

        var lineIndex = source[..markerIdx].Count(c => c == '\n');
        var start = 0;
        for (var i = 0; i < lineIndex; i++)
            start = source.IndexOf('\n', start) + 1;

        var next = source.IndexOf('\n', start);
        var end = next == -1 ? source.Length : next + 1;
        return (start, end);
    }
}
