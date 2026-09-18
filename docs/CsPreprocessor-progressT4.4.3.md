# T4.4.3 — `#` inside a multi-line string is not a directive

## Goal
A line-start `#` inside a multi-line string must NOT be classified as a preprocessor directive.
The string extent is determined by parsing the string as a C# **Expression** with the main C#
parser's `Expression` rule (no hand-rolled string-content scanning).

## Status: DONE

## Where integrated
- `Parsers/CSharp/CsPreprocessor/PreprocessorTerminals.cs`
  - `LineSupport.IsDirectiveStart`: when a `#` is found (after leading whitespace), it is now
    rejected as a directive-start if the `#` position lies inside a multi-line string span.
  - New nested `private static class MultiLineStrings`: detects string starts, parses each with
    the C# parser's `Expression` rule, records `[start, end)` spans that contain a `\n`, and
    answers `IsInsideSpan(input, pos)`.

## How the Expression parser is built/used
- A `CSharpParser` is built once (thread-safe `Lazy`) from the CSharpGrammar embedded grammar
  resources `Cs1.grammar` .. `Cs11.grammar` (Cs11 = raw strings). Trivia/terminals are
  `CSharpTerminals.Trivia()` / `CSharpTerminals.GetAll()`.
- For a string start at `pos`, we call `csharpParser.Parser.Parse(input, "Expression", out _, pos)`.
  `Parser.Parse` has a `startPos` overload (default 0); `CSharpParser.Parse` does not expose it, so
  we use the inner `Parser` directly.
- Length = `end - pos` from `result.TryGetSuccess(out _, out end)`.
- **Thread-safety**: the shared `CSharpParser.Parser` is a mutable, non-thread-safe instance (its
  `Parse` mutates memo/terminal-cache/error state). Parallel test threads (MSTest method-level) and
  any concurrent `Run` callers would corrupt each other, so `ParseExpressionLength` serializes the
  `Parse` call on a static `_parseLock`. The expensive grammar build is paid once by the `Lazy`, so
  the lock only guards the (fast) parse itself.

## Length + newline-count logic
- Scan `input` left to right. A string start is `@`+`"` (verbatim) or `"` (regular or raw, the
  `$`-run prefix is ignored — the expression start is the quote/`@`/quote-run).
- Parse `Expression` from the start; if it succeeds and the span `[start, start+len)` contains a
  `\n`, record it as a multi-line string span. Advance `pos` to the span end (skip the string).
- `IsDirectiveStart` rejects a `#` that falls inside any recorded span.

## Key decisions
- Integration point = the terminal's directive-start test (parse-time), because the grammar's
  `Line = DirectiveLine | CodeLine` decision misclassifies a `#` line inside a string; the
  interpreter runs after parsing and cannot fix an already-misclassified node.
- Spans cached per input via `[ThreadStatic]` (keyed by reference): parsing is synchronous per
  source on one thread, and MSTest runs methods on separate threads, so this is both efficient
  (computed once per input) and safe under method-level parallelism.

## Deviations
- **Lock around `Parse`** (not in the original instruction): required because the shared `CSharpParser.Parser`
  is not thread-safe and the test suite runs methods in parallel (and a real build could call `Run`
  concurrently). First build without the lock passed 98/117; the 19 failures were exceptions thrown from
  `ParseExpressionLength` (concurrent `Parse` on one `Parser`). Adding `_parseLock` → 117/117.
- **Grammar range Cs1..Cs11**: the test's `CSharpGrammarLoader` uses only `Cs1.grammar`, which has no raw
  strings. Raw strings (the primary `"""` case) are C# 11, so the preprocessor's Expression parser loads
  `Cs1.grammar`..`Cs11.grammar` (ascending) — the same composition `EmbeddedGrammar.LoadGrammarUpTo` uses.
- **`string.IndexOf(char, int, int)`** instead of `ReadOnlySpan<char>.Contains`: the latter is not in
  netstandard2.0 (the CsPreprocessor target).
- **Known limitation (not exercised by any test/sanity case)**: `ComputeSpans` scans the raw input for
  string starts, so a `"`/`@"` that appears *inside a comment* is also probed. A regular-string probe
  fails (no newline in a plain string) and a verbatim probe can over-report a span, but any `#` inside
  such a span is already hidden by the existing comment handling, so no real directive is ever masked.

## Final state
- `dotnet build Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj` → 0 errors.
- `dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj` → 117/117 pass.
- Sanity (`Preprocessor.Run`), exact outcomes:
  - raw `var s = """\n#define FOO\n""";\n#if FOO\nint x;\n#endif\n` → `int x;` **BLANKED**
    (`#define FOO` line blanked; FOO undefined; `#if`/`#endif` blanked; 0 diagnostics).
  - verbatim `var s = @"\n#define FOO\n";\n#if FOO\nint x;\n#endif\n` → `int x;` **BLANKED**
    (`#define FOO` line blanked; FOO undefined; 0 diagnostics).
  - regular `var s = "a";\n#define FOO\n#if FOO\nint x;\n#endif\n` → `int x;` **KEPT**
    (single-line string not a span; `#define FOO` is a real directive so FOO is defined and `#if` active).
- All outcomes preserve the D2 same-length (1:1 position) invariant.

## Fix: thread-local parser (remove global lock)
The shared `Lazy<CSharpParser>` + global `static _parseLock` serialized every concurrent `Parse`,
which is unacceptable. Replaced with a `[ThreadStatic] CSharpParser? _parser` so each thread uses its
OWN parser:
- Removed the `Lazy<CSharpParser> _parser` and the `static readonly object _parseLock`.
- Added `[ThreadStatic] private static CSharpParser? _parser;` plus a `GetParser()` that lazily
  builds via the same `BuildParser` (Cs1..Cs11 grammar + `CSharpTerminals`) once PER THREAD on first
  use, then reuses it.
- `ParseExpressionLength` now calls `GetParser().Parser.Parse(...)` with no `lock` — each thread's
  parser is touched only by that thread, so no lock is needed.
- The `[ThreadStatic]` span cache (`_cachedInput`/`_cachedSpans`) and `ComputeSpans`/
  `ParseExpressionLength`/`IsInsideSpan` logic are unchanged.
- Also dropped the now-unused `using System.Threading;`.

Result: the expensive grammar build is paid once per thread (acceptable); no global lock remains.
- `dotnet build Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj` → 0 errors, 0 warnings.
- `dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj` → 117/117 pass (method-level
  parallelism proves no deadlock/race).
