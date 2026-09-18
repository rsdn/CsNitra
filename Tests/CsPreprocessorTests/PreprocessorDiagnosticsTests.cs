using CsPreprocessor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsPreprocessorTests;

[TestClass]
public sealed class PreprocessorDiagnosticsTests
{
    // T2.3: Preprocessor.Run emits diagnostics in ORIGINAL source coordinates.
    // #error/#warning fire only in active regions; structural errors (stray
    // #else/#elif/#endif, unterminated #if) are always reported.

    [TestMethod]
    public void Error_Active_EmitsOne()
    {
        const string source = "#error \"boom\"\n";

        var d = ExpectSingle(Run(source));

        Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
        Assert.AreEqual("CS1029", d.Code);
        Assert.AreEqual("boom", d.Message);
        Assert.AreEqual(0, d.StartPos);
        Assert.AreEqual(LineEnd(source, 0), d.EndPos);
    }

    [TestMethod]
    public void Error_Inactive_EmitsNone()
    {
        const string source = "#if A\n#error \"off\"\n#endif\n";

        Assert.AreEqual(0, Run(source).Diagnostics.Count);
    }

    [TestMethod]
    public void Error_Active_ViaDefine()
    {
        const string source = "#define A\n#if A\n#error \"on\"\n#endif\n";

        var d = ExpectSingle(Run(source));

        Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
        Assert.AreEqual("on", d.Message);
    }

    [TestMethod]
    public void Warning_Active_EmitsOne()
    {
        const string source = "#warning \"careful\"\n";

        var d = ExpectSingle(Run(source));

        Assert.AreEqual(DiagnosticSeverity.Warning, d.Severity);
        Assert.AreEqual("CS1030", d.Code);
        Assert.AreEqual("careful", d.Message);
    }

    [TestMethod]
    public void Warning_Inactive_EmitsNone()
    {
        const string source = "#if A\n#warning \"off\"\n#endif\n";

        Assert.AreEqual(0, Run(source).Diagnostics.Count);
    }

    [TestMethod]
    public void Error_MessageForms()
    {
        Assert.AreEqual("boom", ExpectSingle(Run("#error boom\n")).Message);
        Assert.AreEqual("error", ExpectSingle(Run("#error\n")).Message);
        Assert.AreEqual("spaced msg", ExpectSingle(Run("#error \"spaced msg\"\n")).Message);
    }

    [TestMethod]
    public void Stray_Else()
    {
        const string source = "#else\n";

        var d = ExpectSingle(Run(source));

        Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
        Assert.AreEqual("CS1025", d.Code);
        StringAssert.Contains(d.Message, "#else");
        Assert.AreEqual(0, d.StartPos);
    }

    [TestMethod]
    public void Stray_Elif()
    {
        const string source = "#elif A\n";

        var d = ExpectSingle(Run(source));

        Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
        Assert.AreEqual("CS1028", d.Code);
    }

    [TestMethod]
    public void Stray_EndIf()
    {
        const string source = "#endif\n";

        var d = ExpectSingle(Run(source));

        Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
        Assert.AreEqual("CS1023", d.Code);
    }

    [TestMethod]
    public void Unterminated_If()
    {
        const string source = "#if A\nint x;\n";

        var d = ExpectSingle(Run(source));

        Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
        Assert.AreEqual("CS1024", d.Code);
        Assert.AreEqual(0, d.StartPos);
    }

    [TestMethod]
    public void Unterminated_Nested_PointsAtInnermost()
    {
        // Both #if's left open (no #endif): the diagnostic points at the innermost open #if
        // (the most recently opened still-open one), not the outer.
        const string source = "#define A\n#if A\n#if B\nint x;\n";

        var d = ExpectSingle(Run(source));

        Assert.AreEqual("CS1024", d.Code);
        Assert.AreEqual(LineStart(source, 2), d.StartPos);
    }

    [TestMethod]
    public void Balanced_Block_EmitsNone()
    {
        const string source = "#define A\n#if A\nint x;\n#else\nint y;\n#endif\n";

        Assert.AreEqual(0, Run(source).Diagnostics.Count);
    }

    [TestMethod]
    public void Positions_AreOriginalCoordinates()
    {
        const string source = "int x = 1;\n#error \"boom\"\n";

        var d = ExpectSingle(Run(source));

        Assert.AreEqual("CS1029", d.Code);
        Assert.AreEqual(LineStart(source, 1), d.StartPos);
        Assert.AreEqual(LineEnd(source, 1), d.EndPos);
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

    private static int LineEnd(string source, int lineIndex)
    {
        var start = LineStart(source, lineIndex);
        var next = source.IndexOf('\n', start);
        return next == -1 ? source.Length : next + 1;
    }
}
