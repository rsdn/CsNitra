using System.Diagnostics;
using System.Text;
using CsPreprocessor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsPreprocessorTests;

// T5.2 — PERFORMANCE smoke check (optional). Builds a LARGE synthetic source (~200k lines / a few MB)
// that exercises the preprocessor: a mix of code lines, #if/#define/#region directives, multi-line
// raw strings ("""..."""), verbatim strings (@"..."), and comments. The preprocessor's MultiLineStrings
// parses a C# Expression for EVERY string start (spans cached per input), so a string-heavy file is the
// worst case; this test measures that cost.
//
// One method (to avoid self-contention under method-level parallelism) performs two checks:
//   (timing)      Preprocessor.Run completes within a generous bound (15s smoke bound, NOT a strict SLO;
//                 machines vary — no exact-time assertion). The actual time is reported via Console
//                 output and in docs/CsPreprocessor-progressT5.2.md.
//   (correctness) D2 same-length at scale + one known active line kept verbatim + one known inactive line
//                 blanked (light sanity so the perf test also checks correctness at scale).
//
// The one-time per-thread C# Expression grammar build (Cs1..Cs11) is paid in an untimed warmup so the
// timed run measures the per-file cost (ComputeSpans + line processing), not the grammar build.
[TestClass]
public sealed class PreprocessorPerformanceTests
{
    // Generous smoke bound (not a strict SLO; machines vary). The preprocessor must handle the large
    // string-heavy file quickly.
    private static readonly TimeSpan TimeBudget = TimeSpan.FromSeconds(15);

    // 10_000 blocks * 20 lines = 200_000 body lines (+ 7 header lines = ~200k total).
    private const int BlockCount = 10_000;

    // Known active line (kept verbatim): ACTIVE_T52 is #define'd immediately above it in the header.
    private const string ActiveLine = "string ActiveMarker_T52 = \"kept\";";

    // Known inactive line (blanked): INACTIVE_T52 is never defined.
    private const string InactiveLine = "string InactiveMarker_T52 = \"blanked\";";

    [TestMethod]
    public void Run_LargeStringHeavyFile_FastAndCorrect()
    {
        var source = BuildLargeSource();

        // Warm up (untimed): build this thread's C# Expression parser (Cs1..Cs11 grammar) and the
        // preprocessor's own parser, so the timed run measures the per-file cost, not the one-time
        // grammar build. The warmup input has a '#' directive + a string to force the C# parser build
        // (MultiLineStrings.GetParser is lazily built on the first '#' encountered).
        Preprocessor.Run("#define W\n#if W\nstring s = \"warmup\";\n#endif\n", Array.Empty<string>());

        var sw = Stopwatch.StartNew();
        var result = Preprocessor.Run(source, Array.Empty<string>());
        sw.Stop();

        var elapsed = sw.Elapsed;
        var lines = source.Count(c => c == '\n');
        Console.WriteLine($"[T5.2] large source: {source.Length} bytes / {lines} lines; Preprocessor.Run took {elapsed.TotalSeconds:F3}s");

        // Correctness at scale: D2 same-length (identity mapping).
        Assert.AreEqual(source.Length, result.Text.Length,
            $"Text.Length ({result.Text.Length}) != source.Length ({source.Length}) (D2 violated at scale)");

        // A known active line is kept verbatim.
        AssertKept(result, source, ActiveLine);

        // A known inactive line is blanked.
        AssertBlanked(result, source, InactiveLine);

        // Timing: within the generous smoke bound.
        Assert.IsTrue(elapsed < TimeBudget,
            $"Preprocessor.Run took {elapsed.TotalSeconds:F3}s (budget {TimeBudget.TotalSeconds:F0}s) on {source.Length} bytes / {lines} lines — performance unacceptable");
    }

    // Builds the deterministic large source: a header with one active and one inactive #if body, then
    // BlockCount repeating 20-line blocks mixing code, directives, strings, raw strings, verbatim
    // strings and comments. Fully deterministic (no randomness).
    private static string BuildLargeSource()
    {
        var sb = new StringBuilder(BlockCount * 420);

        // Header: one active line (kept) and one inactive line (blanked) for the correctness check.
        sb.Append("#define ACTIVE_T52\n");
        sb.Append("#if ACTIVE_T52\n");
        sb.Append(ActiveLine).Append('\n');
        sb.Append("#endif\n");
        sb.Append("#if INACTIVE_T52\n");
        sb.Append(InactiveLine).Append('\n');
        sb.Append("#endif\n");

        for (var k = 0; k < BlockCount; k++)
        {
            sb.Append("#region Block").Append(k).Append('\n');
            sb.Append("#define SYM").Append(k).Append('\n');
            sb.Append("string plain").Append(k).Append(" = \"plain value ").Append(k).Append("\";\n");
            sb.Append("int field").Append(k).Append(" = ").Append(k).Append(";\n");
            sb.Append("// line comment ").Append(k).Append('\n');
            sb.Append("/* block comment ").Append(k).Append(" */\n");
            sb.Append("string verbatim").Append(k).Append(" = @\"verbatim ").Append(k).Append("\";\n");
            sb.Append("#if SYM").Append(k).Append('\n');
            sb.Append("string activeInIf").Append(k).Append(" = \"active ").Append(k).Append("\";\n");
            sb.Append("#else\n");
            sb.Append("string inactiveInIf").Append(k).Append(" = \"inactive ").Append(k).Append("\";\n");
            sb.Append("#endif\n");
            sb.Append("string raw").Append(k).Append(" = \"\"\"\n");
            sb.Append("    raw line one ").Append(k).Append('\n');
            sb.Append("    # hash inside raw string ").Append(k).Append('\n');
            sb.Append("    raw line three ").Append(k).Append('\n');
            sb.Append("    \"\"\";\n");
            sb.Append("var call").Append(k).Append(" = Method").Append(k).Append("();\n");
            sb.Append("#undef SYM").Append(k).Append('\n');
            sb.Append("#endregion\n");
        }

        return sb.ToString();
    }

    private static void AssertKept(PreprocessResult result, string source, string marker)
    {
        var (start, end) = LineSpan(source, marker);
        Assert.AreEqual(
            source.Substring(start, end - start),
            result.Text.Substring(start, end - start),
            $"Line «{marker}» was not kept verbatim");
    }

    private static void AssertBlanked(PreprocessResult result, string source, string marker)
    {
        var (start, end) = LineSpan(source, marker);
        for (var i = start; i < end; i++)
            Assert.IsTrue(
                result.Text[i] is ' ' or '\n' or '\r',
                $"Line «{marker}» is not blanked: unexpected '{result.Text[i]}' (U+{(int)result.Text[i]:X4}) at offset {i}");
    }

    private static (int Start, int End) LineSpan(string source, string marker)
    {
        var markerIdx = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.IsTrue(markerIdx >= 0, $"marker «{marker}» not found in source");
        var start = source.LastIndexOf('\n', Math.Max(0, markerIdx - 1)) + 1;
        var end = source.IndexOf('\n', markerIdx);
        if (end < 0)
            end = source.Length;
        return (start, end);
    }
}
