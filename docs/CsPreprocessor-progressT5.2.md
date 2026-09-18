# CsPreprocessor — T5.2 Progress: performance smoke check on a LARGE file

Sub-point: **T5.2** (Stage 5 — Hardening, *optional*).
Task: verify the preprocessor is fast enough on a LARGE file. The preprocessor's
`MultiLineStrings` parses a C# `Expression` for **every** string start (via a per-thread
`CSharpParser`, spans cached per input), so a string-heavy file is the worst case — measure it.

## Verdict

**PASS — performance acceptable.** `Preprocessor.Run` on the large string-heavy file completes in
**~5.56 s** (budget: 15 s smoke bound). No production code touched. See "Performance assessment"
below for the bottleneck (the per-string-start `Expression` parse), which is the dominant cost but
comfortably within the generous bound.

## Scope / files touched

- **Added** `Tests/CsPreprocessorTests/PreprocessorPerformanceTests.cs` (`[TestClass]`, MSTest, sealed,
  method-level parallelism via the existing `[assembly: Parallelize(MethodLevel)]`).
- **Added** `docs/CsPreprocessor-progressT5.2.md` (this file).
- **No production code touched.** No existing test files modified.

## API used

- `Preprocessor.Run(string source, IEnumerable<string> commandLineSymbols)` → `PreprocessResult`
  (namespace `CsPreprocessor`). `.Text` is the preprocessed (directive-free, same-length) source.
  Command-line symbols passed as `Array.Empty<string>()` — the active/inactive selection in the
  correctness check is driven by in-source `#define` only.

## Synthetic large source (deterministic)

Built in-test by `BuildLargeSource()` (a `StringBuilder` loop, **no randomness**):

- **Size: 4,505,718 bytes / 200,007 lines** (~4.5 MB, ~200k lines).
- **Header (7 lines):** `#define ACTIVE_T52` + `#if ACTIVE_T52` / `#endif` wrapping the known **active**
  line, and `#if INACTIVE_T52` / `#endif` (symbol never defined) wrapping the known **inactive** line.
- **Body (10,000 blocks × 20 lines = 200,000 lines):** each block mixes
  - `#region`/`#endregion`, `#define`/`#undef` (structural + symbol directives),
  - a plain string literal (`"plain value k"`), a verbatim string (`@"verbatim k"`),
  - a balanced `#if SYMk` / `#else` / `#endif` with one active and one inactive code line,
  - a **multi-line raw string** (`"""…"""`, 5 lines) whose body contains a line-start `#` (the T4.4.3
    "`#` inside a multi-line string is not a directive" case, exercised at scale),
  - `//` and `/* */` comments, and plain code lines.
- **~50,002 string starts** in total (5 per block × 10,000 blocks + 2 in the header), so
  `MultiLineStrings.ComputeSpans` performs ~50,002 C# `Expression` parses — the cost under test.

## Test + key assertions

One `[TestMethod]` (`Run_LargeStringHeavyFile_FastAndCorrect`) — a single method (rather than two) so
the two checks share one `Preprocessor.Run` result and do **not** contend with each other under
method-level parallelism. Steps:

1. **Untimed warmup:** `Preprocessor.Run` on a tiny source containing a `#` directive + a string. This
   builds the per-thread C# `Expression` parser (Cs1..Cs11 grammar — the expensive one-time cost) and the
   preprocessor's own parser, so the **timed** run measures the per-file cost, not the grammar build.
2. **Timed run:** `Stopwatch` around `Preprocessor.Run(largeSource, [])`.
3. **Correctness at scale:**
   - **D2 same-length:** `result.Text.Length == source.Length` (identity mapping holds at scale).
   - **Active line kept verbatim:** `string ActiveMarker_T52 = "kept";` (inside `#if ACTIVE_T52`,
     `ACTIVE_T52` defined) is byte-for-byte unchanged.
   - **Inactive line blanked:** `string InactiveMarker_T52 = "blanked";` (inside `#if INACTIVE_T52`,
     never defined) is blanked to spaces.
4. **Timing:** `elapsed < 15 s` (generous smoke bound, **not** a strict SLO — no exact-time assertion;
   machines vary). The actual time is reported below.

## Test result

```
dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj --filter "FullyQualifiedName~PreprocessorPerformanceTests"
  Passed Run_LargeStringHeavyFile_FastAndCorrect
  [T5.2] large source: 4505718 bytes / 200007 lines; Preprocessor.Run took 5,582s   (run 1)
  [T5.2] large source: 4505718 bytes / 200007 lines; Preprocessor.Run took 5,563s   (run 2)

dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj
Passed!  - Failed: 0, Passed: 128, Skipped: 0, Total: 128
```

- **Large-file size:** 4,505,718 bytes / 200,007 lines.
- **Measured time:** **~5.56 s** (5.582 s / 5.563 s across two isolated runs) — **< 15 s budget → PASS**.
  (This is the per-file cost; the one-time Cs1..Cs11 grammar build was excluded via the untimed warmup.)
- **New test:** 1 passed / 0 failed. **Full project:** 128 passed / 0 failed (127 pre-existing + 1 new).

## Performance assessment

- **Bottleneck:** `MultiLineStrings.ComputeSpans` — for **each** of the ~50,002 string starts it runs a
  full C# `Expression` parse (`GetParser().Parser.Parse(input, "Expression", out _, exprStart)`). That is
  the dominant cost: ~5.5 s ÷ ~50k string starts ≈ **~110 µs per `Expression` parse**. The rest (the
  preprocessor's own line-based parse of 200k lines + blanking, and the per-call preprocessor-parser
  rebuild) is comparatively small.
- **Why it's acceptable here:** 5.56 s is well under the 15 s smoke bound for a ~4.5 MB / 200k-line
  string-heavy file, and the span list is **cached per input** (`[ThreadStatic]` keyed by reference), so a
  given file pays this cost once. The cost scales linearly with the number of string starts, not with file
  size per se — a file with many short strings is the worst case, which is exactly what this test builds.
- **Note (out of scope, not a defect):** the per-string-start `Expression` parse is the natural cost of the
  T4.4.3 design ("parse the string as a C# `Expression`" rather than hand-rolled scanning). If a future
  strict SLO were needed, the obvious lever is to avoid re-parsing `Expression` for single-line strings
  (only multi-line spans are recorded, so a cheap single-line check could skip the full parse). That would
  be a production change and is **out of scope for T5.2** — flagged here only as the precise bottleneck.
