using CsPreprocessor;
using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsPreprocessorTests;

// T3.2 — END-TO-END integration: the preprocessor emits a directive-free file via same-length
// blanking, so PreprocessResult.Text has a 1:1 identity mapping with the source (proven in T3.1).
// The whole point of T3.2: feed that Text to the REAL C# parser (CSharpGrammar / CSharpParser) and
// show that (1) blanking does not corrupt the kept code (valid file parses cleanly), and (2) a PEG
// diagnostic reported on Text lands at the SAME offset as in the original file (the 1:1 mapping
// holds AT the error position). A fresh CSharpParser is built per test (no cross-test state).
[TestClass]
public sealed class PreprocessorIntegrationTests
{
    // 1. A representative file with #define/#if/#else/#endif/#region/#endregion/#pragma/#nullable
    //    around a simple class + field/method. FOO is defined (in-source #define, redundantly also a
    //    command-line symbol) so the active branch (class C) is kept and the #else branch (class D)
    //    is blanked. The preprocessed Text must parse cleanly with the real C# parser: blanking the
    //    directive lines and the inactive branch does not corrupt the kept code.
    [TestMethod]
    public void Valid_File_With_Directives_PreprocessedTextParsesCleanly()
    {
        const string source =
            "#define FOO\n" +
            "#if FOO\n" +
            "#region Main\n" +
            "#nullable enable\n" +
            "#pragma warning disable 0162\n" +
            "class C\n" +
            "{\n" +
            "    int Field = 1;\n" +
            "    int Method()\n" +
            "    {\n" +
            "        return Field;\n" +
            "    }\n" +
            "}\n" +
            "#endregion\n" +
            "#else\n" +
            "class D\n" +
            "{\n" +
            "    int Other = 2;\n" +
            "}\n" +
            "#endif\n";

        var preprocess = Preprocessor.Run(source, ["FOO"]);

        Assert.AreEqual(source.Length, preprocess.Text.Length, "Text.Length != source.Length (identity mapping broken)");

        var parser = CSharpGrammarLoader.CreateCSharpParser();
        var parseResult = parser.Parse(preprocess.Text, "Grammar", out _);

        Assert.IsNull(parser.Parser.ErrorInfo, $"Expected no error, got ErrorInfo at pos {parser.Parser.ErrorPos}");
        Assert.IsTrue(parseResult.TryGetSuccess(out var node, out var end),
            $"Expected success, error at pos {parser.Parser.ErrorPos}");
        Assert.IsNotNull(node);
        Assert.AreEqual(preprocess.Text.Length, end, $"end {end} != Text.Length {preprocess.Text.Length} (trailing content not consumed)");
        Assert.AreEqual(0, parser.Parser.RecoveryDiagnostics.Count, "Expected no recovery diagnostics for valid code");
    }

    // 2. THE CORE T3.2 ASSERTION. A deliberate syntax error in ACTIVE code (a field with a missing
    //    initializer, `int x = ;`) is kept by the preprocessor (FOO defined). Parsing the preprocessed
    //    Text must FAIL, and the PEG error offset (parser.Parser.ErrorPos) must align 1:1 with the
    //    original file: the char at the error offset is identical in source and Text, and the offset
    //    falls within the kept `int x = ;` line's span. This proves a diagnostic on Text points at the
    //    real error in the original file. (No brittle exact offset — just the line span + char match.)
    [TestMethod]
    public void Error_In_Active_Code_PositionAlignsWithSource()
    {
        const string source =
            "#define FOO\n" +
            "#if FOO\n" +
            "class C\n" +
            "{\n" +
            "    int x = ;\n" +
            "}\n" +
            "#endif\n";

        var preprocess = Preprocessor.Run(source, ["FOO"]);

        Assert.AreEqual(source.Length, preprocess.Text.Length, "Text.Length != source.Length (identity mapping broken)");

        var parser = CSharpGrammarLoader.CreateCSharpParser();
        var parseResult = parser.Parse(preprocess.Text, "Grammar", out _);

        // The parse must not come out as a clean success (it fails, or carries a FatalError).
        Assert.IsTrue(
            parser.Parser.ErrorInfo is not null || parser.Parser.RecoveryDiagnostics.Count > 0,
            $"Expected the parser to record an error, but it was silent. ErrorPos={parser.Parser.ErrorPos}");

        var errPos = parser.Parser.ErrorPos;
        Assert.IsTrue(errPos >= 0 && errPos < source.Length, $"errPos {errPos} out of range [0, {source.Length})");
        Assert.AreEqual(
            source[errPos], preprocess.Text[errPos],
            $"char at error offset {errPos} differs between source and Text (1:1 mapping broken at the error position): source='{source[errPos]}' text='{preprocess.Text[errPos]}'");

        // The error must be on the kept `int x = ;` line (computed from the original source).
        var errLineIdx = source.IndexOf("int x = ;", StringComparison.Ordinal);
        Assert.IsTrue(errLineIdx >= 0, "error line 'int x = ;' not found in source");
        var lineStart = source.LastIndexOf('\n', errLineIdx) + 1;
        var lineEnd = source.IndexOf('\n', errLineIdx);
        if (lineEnd < 0)
            lineEnd = source.Length;
        Assert.IsTrue(errPos >= lineStart && errPos < lineEnd,
            $"errPos {errPos} not within the 'int x = ;' line span [{lineStart}, {lineEnd})");
    }

    // 3. The error is inside an INACTIVE #if branch (BAR undefined), so the preprocessor blanks that
    //    region (the broken `int x = ;` line included). A small ACTIVE valid class (FOO defined) is
    //    kept alongside, so the preprocessed Text is non-empty. The broken line is blanked to spaces
    //    (gone from the parse), and the Text parses cleanly. This proves blanking removes inactive
    //    (even broken) code from the parse.
    //
    // NOTE: the pure "same shape as test 2" (the ONLY class in an inactive branch) would blank the
    //    entire file to pure trivia, which the CSharpParser rejects — it does not accept an empty /
    //    trivia-only compilation unit (even "" fails; see progress doc). That is a pre-existing
    //    CSharpParser limitation, not a preprocessor bug, so this test keeps a bit of active code to
    //    isolate the blanking claim.
    [TestMethod]
    public void Error_In_Inactive_Region_BlankedAway_TextParsesCleanly()
    {
        const string source =
            "#define FOO\n" +
            "#if FOO\n" +
            "class C\n" +
            "{\n" +
            "    int Field = 1;\n" +
            "}\n" +
            "#endif\n" +
            "#if BAR\n" +
            "class D\n" +
            "{\n" +
            "    int x = ;\n" +
            "}\n" +
            "#endif\n";

        // FOO is defined (active: class C kept); BAR is undefined (inactive: broken class D blanked).
        var preprocess = Preprocessor.Run(source, ["FOO"]);

        Assert.AreEqual(source.Length, preprocess.Text.Length, "Text.Length != source.Length (identity mapping broken)");

        // The broken `int x = ;` line (in the inactive region) is blanked to spaces in the Text.
        var errLineIdx = source.IndexOf("int x = ;", StringComparison.Ordinal);
        Assert.IsTrue(errLineIdx >= 0, "error line 'int x = ;' not found in source");
        var lineStart = source.LastIndexOf('\n', errLineIdx) + 1;
        var lineEnd = source.IndexOf('\n', errLineIdx);
        if (lineEnd < 0)
            lineEnd = source.Length;
        var blankedSlice = preprocess.Text.Substring(lineStart, lineEnd - lineStart);
        Assert.IsTrue(blankedSlice.All(c => c is ' ' or '\n' or '\r'),
            $"inactive broken line not blanked in Text: «{blankedSlice}»");

        // The preprocessed Text (active valid class C, broken class D blanked) parses cleanly.
        var parser = CSharpGrammarLoader.CreateCSharpParser();
        var parseResult = parser.Parse(preprocess.Text, "Grammar", out _);

        Assert.IsNull(parser.Parser.ErrorInfo, $"Expected no error, got ErrorInfo at pos {parser.Parser.ErrorPos}");
        Assert.IsTrue(parseResult.TryGetSuccess(out var node, out var end),
            $"Expected success (inactive broken code blanked), error at pos {parser.Parser.ErrorPos}");
        Assert.IsNotNull(node);
        Assert.AreEqual(preprocess.Text.Length, end, $"end {end} != Text.Length {preprocess.Text.Length} (trailing content not consumed)");
        Assert.AreEqual(0, parser.Parser.RecoveryDiagnostics.Count, "Expected no recovery diagnostics for blanked code");
    }
}
