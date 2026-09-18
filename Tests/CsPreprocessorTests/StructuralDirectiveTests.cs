using CsPreprocessor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsPreprocessorTests;

// T4.1: the full set of structural preprocessor diagnostics, verified against the directive
// sequence (active/inactive does not matter for structural checks). Covers the T2.3 set
// (stray #else/#elif/#endif, unterminated #if) plus the T4.1 additions (multiple #else =
// CS1034, #elif after #else = CS1035), including the nested-#if case where an inner frame's
// #else must not leak into the outer frame.
[TestClass]
public sealed class StructuralDirectiveTests
{
    [TestMethod]
    public void Multiple_Else_CS1034()
    {
        const string source = "#define A\n#if A\n#else\n#else\n#endif\n";

        var d = ExpectSingle(Run(source));

        Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
        Assert.AreEqual("CS1034", d.Code);
        // The SECOND #else is on line 3 (0-based).
        Assert.AreEqual(LineStart(source, 3), d.StartPos);
    }

    [TestMethod]
    public void Elif_After_Else_CS1035()
    {
        const string source = "#define A\n#if A\n#else\n#elif B\n#endif\n";

        var d = ExpectSingle(Run(source));

        Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
        Assert.AreEqual("CS1035", d.Code);
        // The #elif is on line 3 (0-based).
        Assert.AreEqual(LineStart(source, 3), d.StartPos);
    }

    [TestMethod]
    public void Single_Valid_Else_NoDiagnostic()
    {
        const string source = "#define A\n#if A\n#else\n#endif\n";

        Assert.AreEqual(0, Run(source).Diagnostics.Count);
    }

    [TestMethod]
    public void Nested_Inner_Else_DoesNotLeak_NoDiagnostic()
    {
        const string source = "#define A\n#if A\n#if A\n#else\n#endif\n#endif\n";

        Assert.AreEqual(0, Run(source).Diagnostics.Count);
    }

    [TestMethod]
    public void Nested_Inner_Multiple_Else_CS1034_AtInner()
    {
        const string source = "#define A\n#if A\n#if A\n#else\n#else\n#endif\n#endif\n";

        var d = ExpectSingle(Run(source));

        Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
        Assert.AreEqual("CS1034", d.Code);
        // The inner second #else is on line 4 (0-based); the outer #if is on line 1.
        Assert.AreEqual(LineStart(source, 4), d.StartPos);
    }

    [TestMethod]
    public void Stray_Else_CS1025()
    {
        const string source = "#else\n";

        var d = ExpectSingle(Run(source));

        Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
        Assert.AreEqual("CS1025", d.Code);
        Assert.AreEqual(0, d.StartPos);
    }

    [TestMethod]
    public void Stray_Elif_CS1028()
    {
        const string source = "#elif A\n";

        var d = ExpectSingle(Run(source));

        Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
        Assert.AreEqual("CS1028", d.Code);
        Assert.AreEqual(0, d.StartPos);
    }

    [TestMethod]
    public void Stray_EndIf_CS1023()
    {
        const string source = "#endif\n";

        var d = ExpectSingle(Run(source));

        Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
        Assert.AreEqual("CS1023", d.Code);
        Assert.AreEqual(0, d.StartPos);
    }

    [TestMethod]
    public void Unterminated_If_CS1024()
    {
        const string source = "#if A\nint x;\n";

        var d = ExpectSingle(Run(source));

        Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
        Assert.AreEqual("CS1024", d.Code);
        // The #if is on line 0.
        Assert.AreEqual(0, d.StartPos);
    }

    [TestMethod]
    public void Balanced_Nested_Block_NoDiagnostic()
    {
        const string source = "#define A\n#if A\n#if A\nint x;\n#else\nint y;\n#endif\n#else\nint z;\n#endif\n";

        Assert.AreEqual(0, Run(source).Diagnostics.Count);
    }

    [TestMethod]
    public void Inactive_Region_Multiple_Else_Still_CS1034()
    {
        // A is undefined, so the #if region is inactive. A #else in an inactive region still
        // counts as a #else, so the second #else still fires CS1034.
        const string source = "#if A\n#else\n#else\n#endif\n";

        var d = ExpectSingle(Run(source));

        Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
        Assert.AreEqual("CS1034", d.Code);
        // The second #else is on line 2 (0-based).
        Assert.AreEqual(LineStart(source, 2), d.StartPos);
    }

    private static PreprocessResult Run(string source, params string[] symbols)
        => Preprocessor.Run(source, symbols);

    private static Diagnostic ExpectSingle(PreprocessResult result)
    {
        Assert.AreEqual(1, result.Diagnostics.Count, $"expected exactly 1 diagnostic, got {result.Diagnostics.Count}");
        return result.Diagnostics[0];
    }

    private static int LineStart(string source, int lineIndex)
    {
        var offset = 0;
        for (var i = 0; i < lineIndex; i++)
            offset = source.IndexOf('\n', offset) + 1;
        return offset;
    }
}
