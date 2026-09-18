# CsPreprocessor — T3.2 Progress: end-to-end integration tests (preprocessed Text → C# parser)

Sub-point: **T3.2** (Stage 3 — Сохранение позиций, END-TO-END интеграция).
Task: prove that the preprocessor's directive-free output (`PreprocessResult.Text`) can be fed to the
REAL C# parser (`CSharpGrammar` / `CSharpParser`) and that (1) blanking does not corrupt the kept code
(valid file parses cleanly), and (2) a PEG diagnostic reported on `Text` lands at the SAME offset as in
the original file (the 1:1 identity mapping — proven in T3.1 — holds AT the error position).

## Scope / files touched

- **Added** `Tests/CsPreprocessorTests/CSharpGrammarLoader.cs` — builds a `CSharpParser` by loading the
  embedded `Cs1.grammar` resource from the `CSharpGrammar` assembly via reflection (the test project does
  NOT reference `CSharpGrammarTests`, so the `EmbeddedGrammar` helper there is unavailable).
- **Added** `Tests/CsPreprocessorTests/PreprocessorIntegrationTests.cs` (`[TestClass]`, MSTest, sealed,
  method-level parallelism via the existing `[assembly: Parallelize(MethodLevel)]`).
- **No production code touched.**

## API used

- `Preprocessor.Run(string source, string[] symbols)` → `PreprocessResult` (namespace `CsPreprocessor`).
  `.Text` is the preprocessed (directive-free, same-length) source.
- `CSharpGrammar.CSharpParser` constructed as
  `new CSharpParser(grammars, CSharpTerminals.Trivia(), CSharpTerminals.GetAll())`, where
  `grammars` is `IReadOnlyList<(string Text, string Path)>` = `[(Cs1.grammar text, "Cs1.grammar")]`.
  `Cs1.grammar` is used (a class/field/method is C# 1.0).
- Parse: `var result = parser.Parse(text, "Grammar", out int end);` (start rule `"Grammar"`).
- On success: `result.TryGetSuccess(out var node, out var end)` is true, `end == text.Length`,
  `parser.Parser.ErrorInfo` is null, `parser.Parser.RecoveryDiagnostics.Count == 0`.
- On failure: `parser.Parser.ErrorPos` (int, 0-based) is the error offset and `parser.Parser.ErrorInfo`
  is non-null.
- A **fresh** `CSharpParser` is built per test (established pattern; avoids cross-test state).

## Tests + key assertions

### 1. `Valid_File_With_Directives_PreprocessedTextParsesCleanly`

A representative file using `#define` / `#if` / `#else` / `#endif` / `#region` / `#endregion` / `#pragma`
/ `#nullable` around a simple `class C { int Field = 1; int Method() { return Field; } }`. `FOO` is
defined (in-source `#define`, redundantly also a command-line symbol) so the active branch (`class C`) is
kept and the `#else` branch (`class D`) is blanked.

Assertions:
- `preprocess.Text.Length == source.Length` (sanity).
- `parser.Parse(preprocess.Text, "Grammar", out _)` succeeds: `parser.Parser.ErrorInfo` is null,
  `result.TryGetSuccess(out var node, out var end)` is true, `node` non-null, `end == preprocess.Text.Length`,
  `parser.Parser.RecoveryDiagnostics.Count == 0`.

Proves the preprocessor produces valid C# (blanking the directive lines and the inactive branch does not
corrupt the kept code).

### 2. `Error_In_Active_Code_PositionAlignsWithSource`  ← the core T3.2 assertion

A deliberate syntax error in ACTIVE code (a field with a missing initializer, `int x = ;`), kept by the
preprocessor (`FOO` defined):

```
#define FOO
#if FOO
class C
{
    int x = ;
}
#endif
```

Assertions:
- `preprocess.Text.Length == source.Length` (sanity).
- The parse does NOT come out as a clean success:
  `!result.TryGetSuccess(out _, out _) || parser.Parser.ErrorInfo is not null`.
- `errPos = parser.Parser.ErrorPos`; `errPos` is in range: `0 <= errPos < source.Length`.
- **The 1:1 mapping holds AT the error position:** `source[errPos] == preprocess.Text[errPos]`.
- The error is on the kept `int x = ;` line: `errPos` falls within that line's span, computed from the
  original `source` (line start = after the previous `\n`, line end = the line's `\n`). No brittle exact
  offset is hardcoded — only the line span + char match.

Observed (verified by a temporary reveal, then removed): `errPos = 42` (the `;` in `int x = ;`),
error-line span `[30, 43)`, `source[42] == preprocess.Text[42] == ';'`. The diagnostic on `Text` points at
the real error in the original file.

### 3. `Error_In_Inactive_Region_BlankedAway_TextParsesCleanly`

The error is inside an INACTIVE `#if BAR` branch (`BAR` undefined), so the preprocessor blanks that region
(the broken `int x = ;` line included). A small ACTIVE valid `class C` (`FOO` defined) is kept alongside so
the preprocessed Text is non-empty.

Assertions:
- `preprocess.Text.Length == source.Length` (sanity).
- The broken `int x = ;` line is blanked to spaces in `Text` (every char in that line's span is a space or
  newline) — the error is gone from the parse.
- `parser.Parse(preprocess.Text, "Grammar", out _)` succeeds: `parser.Parser.ErrorInfo` is null,
  `result.TryGetSuccess(out var node, out var end)` is true, `node` non-null, `end == preprocess.Text.Length`,
  `parser.Parser.RecoveryDiagnostics.Count == 0`.

Proves blanking removes inactive (even broken) code from the parse.

## Test result

```
dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj
Passed!  - Failed: 0, Passed: 78, Skipped: 0, Total: 78
```

- New `PreprocessorIntegrationTests`: **3 passed / 0 failed** (verified via
  `--filter "FullyQualifiedName~PreprocessorIntegrationTests"`).
- Full project: **78 passed / 0 failed** (75 pre-existing + 3 new).

## Production bug found

**Preprocessor: none.** The core T3.2 guarantees hold — valid preprocessed code parses cleanly, and the PEG
error offset aligns 1:1 with the original file at the error position. No preprocessor code changes were
needed or made.

**Related finding (pre-existing `CSharpParser` limitation, NOT a preprocessor bug):** the `CSharpParser`
rejects an **empty / trivia-only** compilation unit. Verified directly: `parser.Parse("", "Grammar", out _)`
fails (`ErrorInfo` at pos 0), and any whitespace-only input (e.g. `"   "`, `"\n"`, a string of spaces+newlines)
also fails with `ErrorInfo` at the EOF position and `RecoveryDiagnostics.Count == 0`. The grammar declares
`Grammar = CompilationUnit;` and `CompilationUnit = NamespaceMember*;` (zero-or-more), so an empty file —
which is valid C# — is *supposed* to parse, but the parser does not accept zero members.

Consequence for this task: the literal "same shape as test 2" version of test 3 (the ONLY class inside an
inactive `#if`) blanks the entire file to pure trivia, which the `CSharpParser` then rejects. This is a
downstream `CSharpParser` limitation, not a preprocessor defect (the preprocessor correctly blanked the
inactive code and preserved the length). To keep test 3 meaningful and passing, a small active valid class is
kept alongside the inactive broken class, isolating the actual claim (blanking removes broken inactive code).

Recommended follow-up (out of T3.2 scope): make `CSharpParser`/`CompilationUnit` accept an empty
compilation unit so a fully-inactive file preprocesses to a parseable (empty) unit.
