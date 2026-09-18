using CsPreprocessor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsPreprocessorTests;

// T4.2: Inactive directives mirror Roslyn. An inactive #define/#undef has NO effect on the
// symbol table (DirectiveStack.Define/Undef gate on IsActive), and an inactive #error/#warning
// does NOT fire (the interpreter gates on IsActive). Active counterparts are included as the
// contrast for each case.
[TestClass]
public sealed class InactiveDirectiveTests
{
    [TestMethod]
    public void Inactive_Define_HasNoEffect()
    {
        // A is undefined, so the #if A region is inactive and the #define B has no effect.
        // B is therefore NOT defined, so the #if B region is blanked.
        const string source = "#if A\n#define B\n#endif\n#if B\nint x;\n#endif\n";

        var result = Run(source);

        AssertBlanked(result, source, "int x;");
    }

    [TestMethod]
    public void Active_Define_HasEffect()
    {
        // A is defined (active #define), so the #if A region is active and the #define B takes
        // effect. B IS defined, so the #if B region is kept.
        const string source = "#define A\n#if A\n#define B\n#endif\n#if B\nint x;\n#endif\n";

        var result = Run(source);

        AssertKept(result, source, "int x;");
    }

    [TestMethod]
    public void Inactive_Undef_HasNoEffect()
    {
        // A is undefined, so the #if A region is inactive and the #undef B has no effect.
        // B (defined above) stays defined, so the #if B region is kept.
        const string source = "#define B\n#if A\n#undef B\n#endif\n#if B\nint x;\n#endif\n";

        var result = Run(source);

        AssertKept(result, source, "int x;");
    }

    [TestMethod]
    public void Active_Undef_HasEffect()
    {
        // A is defined (active #define), so the #if A region is active and the #undef B takes
        // effect. B becomes undefined, so the #if B region is blanked.
        const string source = "#define A\n#define B\n#if A\n#undef B\n#endif\n#if B\nint x;\n#endif\n";

        var result = Run(source);

        AssertBlanked(result, source, "int x;");
    }

    [TestMethod]
    public void Inactive_Error_DoesNotFire()
    {
        // A is undefined, so the #if A region is inactive and the #error does not fire.
        const string source = "#if A\n#error \"off\"\n#endif\n";

        var result = Run(source);

        Assert.AreEqual(0, result.Diagnostics.Count);
    }

    [TestMethod]
    public void Active_Error_Fires()
    {
        // A is defined (active #define), so the #if A region is active and the #error fires.
        const string source = "#define A\n#if A\n#error \"on\"\n#endif\n";

        var result = Run(source);

        Assert.AreEqual(1, result.Diagnostics.Count);
        Assert.AreEqual("CS1029", result.Diagnostics[0].Code);
        Assert.AreEqual(DiagnosticSeverity.Error, result.Diagnostics[0].Severity);
    }

    [TestMethod]
    public void Inactive_Warning_DoesNotFire()
    {
        // A is undefined, so the #if A region is inactive and the #warning does not fire.
        const string source = "#if A\n#warning \"off\"\n#endif\n";

        var result = Run(source);

        Assert.AreEqual(0, result.Diagnostics.Count);
    }

    [TestMethod]
    public void Active_Warning_Fires()
    {
        // A is defined (active #define), so the #if A region is active and the #warning fires.
        const string source = "#define A\n#if A\n#warning \"on\"\n#endif\n";

        var result = Run(source);

        Assert.AreEqual(1, result.Diagnostics.Count);
        Assert.AreEqual("CS1030", result.Diagnostics[0].Code);
        Assert.AreEqual(DiagnosticSeverity.Warning, result.Diagnostics[0].Severity);
    }

    [TestMethod]
    public void Nested_Inactive_Define_HasNoEffect()
    {
        // A is undefined, so both nested #if A regions are inactive and the #define B has no
        // effect. B is therefore NOT defined, so the #if B region is blanked.
        const string source = "#if A\n#if A\n#define B\n#endif\n#endif\n#if B\nint x;\n#endif\n";

        var result = Run(source);

        AssertBlanked(result, source, "int x;");
    }

    [TestMethod]
    public void Define_ThenActive_Undef_ThenUse_IsBlanked()
    {
        // A is defined (active #define) then undefined (active #undef); both take effect. A is
        // therefore undefined at the #if A, so the region is blanked.
        const string source = "#define A\n#undef A\n#if A\nint x;\n#endif\n";

        var result = Run(source);

        AssertBlanked(result, source, "int x;");
    }

    private static PreprocessResult Run(string source, params string[] symbols)
        => Preprocessor.Run(source, symbols);

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
