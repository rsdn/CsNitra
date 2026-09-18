# CsPreprocessor — T3.1 Progress: position-preservation tests

Sub-point: **T3.1** (Stage 3 — Сохранение позиций, ОТДЕЛЬНЫЕ тесты).
Task: prove the identity/1:1 position-mapping guarantee (D2 same-length blanking) over a
representative set of inputs, with dedicated tests separate from the interpreter-behavior tests.

## Scope / files touched

- **Added** `Tests/CsPreprocessorTests/PositionPreservationTests.cs` (`[TestClass]`, MSTest, sealed,
  method-level parallelism via the existing `[assembly: Parallelize(MethodLevel)]`).
- No production code touched.

## The 1:1 invariant helper

`AssertOneToOne(string source, PreprocessResult result)` asserts, for one input:

1. `result.Text.Length == source.Length` (same length → identity offset mapping).
2. For every index `i` in `[0, source.Length)`:
   - if `source[i]` is `'\n'` or `'\r'` → `result.Text[i] == source[i]` (newlines preserved, same position);
   - else if `result.Text[i] == ' '` → ok (blanked);
   - else → `result.Text[i] == source[i]` (kept verbatim, same position).
3. Newline positions coincide: for every `i`, `(source[i] == '\n') == (result.Text[i] == '\n')`.

Rationale: a kept char is by definition `source[i]` and not a space; a blanked char is a space.
The only chars that may differ are non-newline chars, which are either kept-identical or blanked-to-space.
Because `Text.Length == source.Length`, a position in `Text` is the SAME position in `source`, so a PEG
diagnostic reported on `Text` aligns 1:1 with the original file.

Every test runs `Preprocessor.Run(source, symbols)` and calls `AssertOneToOne`.

## Inputs covered (one `[TestMethod]` each)

| # | Test | Input shape | Kept / blanked expectation |
|---|------|-------------|----------------------------|
| 1 | `No_Directives_FullyKept` | pure code, 3 lines | all kept |
| 2 | `Active_Directives_Only_CodeKept` | `#define FOO` / `#if true` / `#endif` around code | directive lines blanked, code kept |
| 3 | `Inactive_Region_Blanked_CodeOutsideKept` | `#if A` (A undefined) … `#endif` + code outside | inside blanked, outside kept |
| 4 | `Mixed_ActiveInactive_ElseBranch` | `#if A` / `#else` both branches (A undefined) | if-branch blanked, else-branch kept |
| 5 | `Nested_If_InnerInactive` | `#define A` / `#if A` / `#if B` (B undefined) … nested | inner code blanked, outer code kept |
| 6 | `Define_Undef_DriveActiveInactive` | `#define A` … `#undef A` driving two `#if A` | before-undef kept, after-undef blanked |
| 7 | `Misc_Directives_InterleavedWithCode` | `#error`/`#warning`/`#region`/`#endregion`/`#pragma`/`#nullable`/`#line` interleaved with code | all directive lines blanked, code kept |
| 8 | `CRLF_Newlines_PreservedAtSamePositions` | same logical mixed input with `\r\n` newlines | both `\r` and `\n` preserved at same positions, length unchanged |
| 9 | `Indented_Directives_And_Code` | `   #if A` (ws before `#`) + indented code lines | directive + inactive indented code blanked, normal code kept |

Case 8 additionally asserts, explicitly, that every `\r` coincides: `(source[i]=='\r') == (Text[i]=='\r')`
for all `i` (on top of `AssertOneToOne`, which already covers `\n` and `\r` per-char).

## Concrete spot-checks (identity mapping at specific offsets)

For the **mixed** (case 4) and **nested** (case 5) cases, beyond `AssertOneToOne`:

- `AssertLineByteIdentical(source, text, lineIndex)` — a known-**kept** line is byte-identical at the
  SAME offset in both `Text` and `source` (compares `source[start..end]` == `text[start..end]`, where
  `[start,end)` is the line's byte range including its trailing `\n`).
  - Case 4: line 3 `int in_else;` (kept else-branch).
  - Case 5: line 5 `int outer;` (kept outer-scope code).
- `AssertLineBlankedAtOffset(source, text, lineIndex)` — a known-**blanked** line is all-spaces
  (except newlines) at the SAME offset, and the source line actually had blankable content (non-vacuous).
  - Case 4: line 1 `int in_if;` (inactive if-branch).
  - Case 5: line 3 `int inner;` (inactive inner code).

Line byte ranges are computed by `LineBounds` (split on `\n`), not hardcoded, so the checks are robust.

## Test result

```
dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj
Passed!  - Failed: 0, Passed: 75, Skipped: 0, Total: 75
```

- New `PositionPreservationTests`: **9 passed / 0 failed** (verified via
  `--filter "FullyQualifiedName~PositionPreservationTests"`).
- Full project: **75 passed / 0 failed** (66 pre-existing + 9 new).

## Production bug found

**None.** The identity/1:1 position-mapping guarantee holds for every representative input:
`Text.Length == source.Length`, newlines preserved at the same positions, and every other char is
either kept verbatim at the same offset or blanked to a space. No production code changes were needed.
