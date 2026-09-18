using CsPreprocessor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsPreprocessorTests;

[TestClass]
public sealed class LineDirectiveTests
{
    // T4.3: Preprocessor.Run records active #line directives into
    // PreprocessResult.LineDirectives (D5: display remap only — does not affect offsets).
    // Each LineDirective captures OriginalLine (1-based original source line of the #line),
    // MappedLine, FilePath, and State. Inactive and malformed #line directives are not recorded.
    // All sources below use LF newlines.

    [TestMethod]
    public void Line_NumberOnly()
    {
        var d = ExpectSingle(Run("#line 42\n"));

        Assert.AreEqual(1, d.OriginalLine);
        Assert.AreEqual(42, d.MappedLine);
        Assert.IsNull(d.FilePath);
        Assert.AreEqual(LineDirectiveState.Remapped, d.State);
    }

    [TestMethod]
    public void Line_NumberAndFile()
    {
        var d = ExpectSingle(Run("#line 42 \"file.cs\"\n"));

        Assert.AreEqual(1, d.OriginalLine);
        Assert.AreEqual(42, d.MappedLine);
        Assert.AreEqual("file.cs", d.FilePath);
        Assert.AreEqual(LineDirectiveState.Remapped, d.State);
    }

    [TestMethod]
    public void Line_Default()
    {
        var d = ExpectSingle(Run("#line default\n"));

        Assert.AreEqual(1, d.OriginalLine);
        Assert.IsNull(d.MappedLine);
        Assert.IsNull(d.FilePath);
        Assert.AreEqual(LineDirectiveState.Default, d.State);
    }

    [TestMethod]
    public void Line_Hidden()
    {
        var d = ExpectSingle(Run("#line hidden\n"));

        Assert.AreEqual(1, d.OriginalLine);
        Assert.IsNull(d.MappedLine);
        Assert.IsNull(d.FilePath);
        Assert.AreEqual(LineDirectiveState.Hidden, d.State);
    }

    [TestMethod]
    public void Line_NumberHidden()
    {
        var d = ExpectSingle(Run("#line 7 hidden\n"));

        Assert.AreEqual(1, d.OriginalLine);
        Assert.AreEqual(7, d.MappedLine);
        Assert.IsNull(d.FilePath);
        Assert.AreEqual(LineDirectiveState.Hidden, d.State);
    }

    [TestMethod]
    public void Line_NumberFileHidden()
    {
        var d = ExpectSingle(Run("#line 7 \"f.cs\" hidden\n"));

        Assert.AreEqual(1, d.OriginalLine);
        Assert.AreEqual(7, d.MappedLine);
        Assert.AreEqual("f.cs", d.FilePath);
        Assert.AreEqual(LineDirectiveState.Hidden, d.State);
    }

    [TestMethod]
    public void Line_QuotedPathWithSpaces()
    {
        var d = ExpectSingle(Run("#line 3 \"my file.cs\"\n"));

        Assert.AreEqual(1, d.OriginalLine);
        Assert.AreEqual(3, d.MappedLine);
        Assert.AreEqual("my file.cs", d.FilePath);
        Assert.AreEqual(LineDirectiveState.Remapped, d.State);
    }

    [TestMethod]
    public void Line_OriginalLineOnLaterLine()
    {
        var d = ExpectSingle(Run("int x;\nint y;\n#line 99 \"f.cs\"\n"));

        Assert.AreEqual(3, d.OriginalLine);
        Assert.AreEqual(99, d.MappedLine);
        Assert.AreEqual("f.cs", d.FilePath);
        Assert.AreEqual(LineDirectiveState.Remapped, d.State);
    }

    [TestMethod]
    public void Line_Multiple_InOrder()
    {
        var result = Run("#line 10\n#line 20\n");

        Assert.AreEqual(2, result.LineDirectives.Count);

        var first = result.LineDirectives[0];
        Assert.AreEqual(1, first.OriginalLine);
        Assert.AreEqual(10, first.MappedLine);
        Assert.IsNull(first.FilePath);
        Assert.AreEqual(LineDirectiveState.Remapped, first.State);

        var second = result.LineDirectives[1];
        Assert.AreEqual(2, second.OriginalLine);
        Assert.AreEqual(20, second.MappedLine);
        Assert.IsNull(second.FilePath);
        Assert.AreEqual(LineDirectiveState.Remapped, second.State);
    }

    [TestMethod]
    public void Line_Inactive_NotRecorded()
    {
        Assert.AreEqual(0, Run("#if A\n#line 42\n#endif\n").LineDirectives.Count);
    }

    [TestMethod]
    public void Line_Malformed_NotRecorded()
    {
        Assert.AreEqual(0, Run("#line\n").LineDirectives.Count);
        Assert.AreEqual(0, Run("#line abc\n").LineDirectives.Count);
    }

    [TestMethod]
    public void Line_FileOnly_NoOp()
    {
        Assert.AreEqual(0, Run("#line \"file.cs\"\n").LineDirectives.Count);
    }

    private static PreprocessResult Run(string source, params string[] symbols)
        => Preprocessor.Run(source, symbols);

    private static LineDirective ExpectSingle(PreprocessResult result)
    {
        Assert.AreEqual(1, result.LineDirectives.Count, $"expected exactly 1 line directive, got {result.LineDirectives.Count}");
        return result.LineDirectives[0];
    }
}
